// Shrinking a picked image before it goes to San.
//
// A phone photo is 3-12 MB and 4000px wide. Gemma's vision encoder works from a much
// smaller grid, so the extra pixels buy nothing and cost plenty: the image shares the
// model's 32K context with the system prompt, the live module snapshot and the whole
// tool catalogue, so a large enough picture pushes out San's own knowledge of what it
// can do. Downscaling here — before upload — keeps the request small, the wait short,
// and the context intact.
//
// 896px on the long edge is comfortably above what the encoder resolves and well
// under anything that hurts. JPEG at 0.85 is visually indistinguishable at this size.
const MAX_EDGE = 896;
const QUALITY = 0.85;

export interface PickedImage {
  dataUrl: string;
  width: number;
  height: number;
  bytes: number;      // approximate decoded size of the base64 payload
}

export async function downscaleImage(file: File): Promise<PickedImage> {
  if (!file.type.startsWith('image/')) throw new Error('That file is not an image.');

  // createImageBitmap decodes off the main thread and, unlike an <img> + onload dance,
  // applies EXIF orientation itself — so a photo taken in portrait doesn't arrive
  // sideways. Falls back for browsers without the imageOrientation option.
  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  } catch {
    bitmap = await createImageBitmap(file);
  }

  const scale = Math.min(1, MAX_EDGE / Math.max(bitmap.width, bitmap.height));
  const width = Math.max(1, Math.round(bitmap.width * scale));
  const height = Math.max(1, Math.round(bitmap.height * scale));

  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Could not process that image.');
  ctx.drawImage(bitmap, 0, 0, width, height);
  bitmap.close?.();

  // Always re-encode as JPEG, including for PNG input. A screenshot saved as PNG can
  // be several times larger than the same image as JPEG at this size, and the model
  // cannot tell the difference. Transparency flattens to black, which is the normal
  // trade for this kind of upload.
  const dataUrl = canvas.toDataURL('image/jpeg', QUALITY);
  const base64 = dataUrl.slice(dataUrl.indexOf(',') + 1);

  return { dataUrl, width, height, bytes: Math.round(base64.length * 0.75) };
}
