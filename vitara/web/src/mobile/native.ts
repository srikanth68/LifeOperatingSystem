import { Capacitor } from '@capacitor/core';

// The few things that differ when Vitara runs inside the native iOS app (Capacitor) rather than
// in a browser. Everything else is the same code: the native app is the web app, bundled.

export const isNative = (): boolean => Capacitor.isNativePlatform();

// The status bar (clock, battery) sits over the page in the native app. Its text has to be light
// on the dark theme and dark on the light one, or the clock disappears into the background.
export async function setNativeChrome(theme: 'dark' | 'light'): Promise<void> {
  if (!isNative()) return;
  try {
    const { StatusBar, Style } = await import('@capacitor/status-bar');
    await StatusBar.setStyle({ style: theme === 'dark' ? Style.Dark : Style.Light });
  } catch {
    /* an older build without the plugin: the app still works, the clock is just harder to read */
  }
}
