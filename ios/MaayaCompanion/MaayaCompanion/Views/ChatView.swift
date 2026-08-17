import SwiftUI
import PhotosUI
import UIKit

// San chat — send a message, see the conversation. Matches SanModule.tsx's
// Assistant tab: GET /api/chat/messages for history, POST /api/chat/messages
// with { content } to send.
struct ChatView: View {
    let client: MaayaClient

    @State private var messages: [ChatMessage] = []
    @State private var draft = ""
    @State private var sending = false
    @State private var loadError: String?

    // Voice: shown only when San's Whisper/Piper proxy is configured.
    @State private var voice: VoiceStatus?
    @State private var showCall = false
    @State private var speech: SpeechPlayer?

    // A picked photo, held until the next send. Only the CURRENT turn ever carries an
    // image: San stores a text marker in history instead, because re-sending the picture
    // on every later turn would spend thousands of vision tokens re-describing something
    // it already answered about.
    @State private var pickerItem: PhotosPickerItem?
    @State private var attachedJpeg: Data?
    @AppStorage("sanAutoSpeak") private var autoSpeak = false

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 12) {
                            if messages.isEmpty && loadError == nil {
                                bubble(role: "assistant", text:
                                    "Hello! I'm San. Ask me anything about your finances, health, or properties — I can see live data across your modules.")
                            }
                            ForEach(messages) { m in
                                bubble(role: m.role, text: m.content).id(m.id)
                            }
                            if sending {
                                bubble(role: "assistant", text: "San is thinking…", italic: true).id("thinking")
                            }
                        }
                        .padding()
                    }
                    .onChange(of: messages) { scrollToBottom(proxy) }
                    .onChange(of: sending) { scrollToBottom(proxy) }
                }

                if let loadError {
                    Text(loadError)
                        .font(.caption)
                        .foregroundStyle(.orange)
                        .padding(.horizontal)
                }

                inputBar
            }
            .navigationTitle("San")
            .toolbar { voiceToolbar }
            .task {
                if speech == nil { speech = SpeechPlayer(client: client) }
                await loadHistory()
                voice = try? await client.voiceStatus()
            }
            .fullScreenCover(isPresented: $showCall) {
                CallView(client: client) {
                    showCall = false
                    Task { await loadHistory() }   // spoken exchange persists server-side
                }
            }
        }
    }

    @ToolbarContentBuilder
    private var voiceToolbar: some ToolbarContent {
        ToolbarItemGroup(placement: .topBarTrailing) {
            if voice?.ttsReady == true {
                Button {
                    autoSpeak.toggle()
                    if !autoSpeak { speech?.stop() }
                } label: {
                    Image(systemName: autoSpeak ? "speaker.wave.2.fill" : "speaker.slash")
                        .foregroundStyle(autoSpeak ? MaayaTheme.gold : .secondary)
                }
            }
            if voice?.callReady == true {
                Button { showCall = true } label: {
                    Image(systemName: "phone.fill").foregroundStyle(MaayaTheme.vitara)
                }
            }
        }
    }

    private var inputBar: some View {
        VStack(spacing: 8) {
            if let attachedJpeg, let ui = UIImage(data: attachedJpeg) {
                HStack {
                    Image(uiImage: ui)
                        .resizable().scaledToFill()
                        .frame(width: 44, height: 44)
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                    Text("Photo attached").font(.caption).foregroundStyle(.secondary)
                    Spacer()
                    Button("Remove") { self.attachedJpeg = nil; pickerItem = nil }
                        .font(.caption)
                }
            }

            HStack(spacing: 10) {
                PhotosPicker(selection: $pickerItem, matching: .images, photoLibrary: .shared()) {
                    Image(systemName: "photo")
                        .padding(10)
                        .background(MaayaTheme.surface, in: Circle())
                }
                .disabled(sending)

                TextField("Ask San anything…", text: $draft, axis: .vertical)
                    .lineLimit(1...4)
                    .padding(10)
                    .background(MaayaTheme.surface, in: RoundedRectangle(cornerRadius: 20))
                    .disabled(sending)

                Button {
                    Task { await send() }
                } label: {
                    Image(systemName: "paperplane.fill")
                        .padding(10)
                        .background(canSend ? MaayaTheme.gold : Color.gray)
                        .foregroundStyle(.black)
                        .clipShape(Circle())
                }
                .disabled(!canSend)
            }
        }
        .padding()
        .background(.ultraThinMaterial)
        .onChange(of: pickerItem) { _, item in
            guard let item else { attachedJpeg = nil; return }
            Task {
                // JPEG at 0.7 keeps a phone photo to a few hundred KB. The raw HEIC off a
                // modern camera is several megabytes, and it rides in the JSON body as
                // base64 -- a third larger again.
                if let data = try? await item.loadTransferable(type: Data.self),
                   let ui = UIImage(data: data),
                   let jpeg = ui.jpegData(compressionQuality: 0.7) {
                    attachedJpeg = jpeg
                }
            }
        }
    }

    // A photo with no caption is a legitimate message, so an attachment alone enables send.
    private var canSend: Bool {
        guard !sending else { return false }
        return attachedJpeg != nil || !draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private func bubble(role: String, text: String, italic: Bool = false) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Text(role == "user" ? "U" : "S")
                .font(.caption).fontWeight(.bold)
                .frame(width: 28, height: 28)
                .background(role == "user" ? MaayaTheme.surface : MaayaTheme.gold.opacity(0.25))
                .foregroundStyle(role == "user" ? .secondary : MaayaTheme.gold)
                .clipShape(Circle())
            Text(text)
                .font(.subheadline)
                .italic(italic)
                .foregroundStyle(italic ? .secondary : .primary)
                .frame(maxWidth: .infinity, alignment: .leading)
                .textSelection(.enabled)
        }
    }

    private func scrollToBottom(_ proxy: ScrollViewProxy) {
        withAnimation {
            if sending { proxy.scrollTo("thinking", anchor: .bottom) }
            else if let last = messages.last { proxy.scrollTo(last.id, anchor: .bottom) }
        }
    }

    private func loadHistory() async {
        do {
            messages = try await client.chatHistory()
            loadError = nil
        } catch {
            loadError = "Can't reach San: \(error.localizedDescription)"
        }
    }

    private func send() async {
        let text = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        let image = attachedJpeg
        // A photo on its own is a real message -- "what is this?" is implied.
        guard !text.isEmpty || image != nil else { return }
        draft = ""
        attachedJpeg = nil
        pickerItem = nil
        sending = true
        loadError = nil
        // Optimistically show the user's message immediately.
        let optimistic = ChatMessage(id: UUID().uuidString, role: "user", content: text, createdAt: .now)
        messages.append(optimistic)
        do {
            let result = try await client.sendChat(
                text,
                imageDataUrl: image.map { "data:image/jpeg;base64," + $0.base64EncodedString() })
            // Replace the optimistic user bubble with the server's canonical pair.
            if let idx = messages.firstIndex(of: optimistic) { messages.remove(at: idx) }
            messages.append(result.userMessage)
            messages.append(result.assistantMessage)
            if autoSpeak, voice?.ttsReady == true {
                Task { await speech?.speak(result.assistantMessage.content) }
            }
        } catch {
            loadError = "Send failed: \(error.localizedDescription)"
        }
        sending = false
    }
}
