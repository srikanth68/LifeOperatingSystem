import SwiftUI

// Probe → PIN pad on a trusted mesh network, else username/password.
// Mirrors the web frontend's Login.tsx / PinPad.tsx behavior.
struct LoginView: View {
    let auth: AuthService

    @State private var probing = true
    @State private var method: AuthMethod = .credentials
    @State private var forceCredentials = false
    @State private var showServerSettings = false

    var body: some View {
        ZStack {
            MaayaTheme.bg.ignoresSafeArea()

            VStack(spacing: 28) {
                header

                if probing {
                    ProgressView()
                        .tint(MaayaTheme.gold)
                        .padding(.top, 20)
                } else if case .pin(let length) = method, !forceCredentials {
                    PinPadView(auth: auth, length: length)
                    Button("Use password instead") { forceCredentials = true }
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                } else {
                    CredentialsForm(auth: auth)
                }

                Spacer()
            }
            .padding()
            .frame(maxWidth: 440)

            VStack {
                HStack {
                    Spacer()
                    Button {
                        showServerSettings = true
                    } label: {
                        Image(systemName: "gearshape.fill")
                            .foregroundStyle(.secondary)
                            .padding(12)
                    }
                }
                Spacer()
            }
        }
        .task { await runProbe() }
        .sheet(isPresented: $showServerSettings) {
            ServerSettingsSheet {
                showServerSettings = false
                Task { await runProbe() }
            }
        }
    }

    private var header: some View {
        VStack(spacing: 6) {
            Image(systemName: "hexagon.fill")
                .font(.system(size: 60))
                .foregroundStyle(MaayaTheme.gold)
                .shadow(color: MaayaTheme.gold.opacity(0.5), radius: 12)
                .padding(.top, 40)
            Text("MAAYA")
                .font(.system(size: 34, weight: .bold, design: .rounded))
                .tracking(8)
            Text("Personal Operating System")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private func runProbe() async {
        probing = true
        let result = await auth.probe()
        if result.trusted && result.method == "pin" && result.pinLength > 0 {
            method = .pin(length: result.pinLength)
        } else {
            method = .credentials
        }
        probing = false
    }
}

// Reachable before sign-in, unlike the full Settings tab (which lives behind
// auth). Lets you point the app at the right host/scheme before attempting
// to log in — otherwise a fresh install is stuck on the default mesh IP with
// no way to reach a local/dev backend first.
private struct ServerSettingsSheet: View {
    let onDone: () -> Void

    @AppStorage("serverHost") private var serverHost = "100.126.41.41"
    @AppStorage("serverScheme") private var serverScheme = "http"

    private let schemes = ["http", "https"]

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Picker("Scheme", selection: $serverScheme) {
                        ForEach(schemes, id: \.self) { Text($0) }
                    }
                    TextField("Host / mesh IP", text: $serverHost)
                        .keyboardType(.URL)
                        .autocorrectionDisabled()
                        .textInputAutocapitalization(.never)
                } header: {
                    Text("Server")
                } footer: {
                    Text("e.g. \"localhost\" if the Maaya backend runs on this same Mac, or the NordVPN Meshnet / Tailscale IP (e.g. 100.126.41.41) for a remote host.")
                        .font(.caption)
                }
            }
            .navigationTitle("Server")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { onDone() }
                }
            }
        }
        .presentationDetents([.medium])
    }
}

private struct PinPadView: View {
    let auth: AuthService
    let length: Int

    @State private var digits = ""
    @State private var error: String?
    @State private var shake = false
    @State private var loading = false

    private let keys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "", "0", "⌫"]

    var body: some View {
        VStack(spacing: 22) {
            Text("Authorization Required")
                .font(.subheadline)
                .foregroundStyle(.secondary)

            HStack(spacing: 16) {
                ForEach(0..<length, id: \.self) { i in
                    Circle()
                        .fill(i < digits.count ? MaayaTheme.gold : Color.white.opacity(0.15))
                        .frame(width: 14, height: 14)
                }
            }
            .offset(x: shake ? -8 : 0)
            .animation(.default.repeatCount(3, autoreverses: true).speed(6), value: shake)

            Text(error ?? " ")
                .font(.caption)
                .foregroundStyle(.red)

            LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: 3), spacing: 16) {
                ForEach(keys, id: \.self) { key in
                    Button {
                        press(key)
                    } label: {
                        Text(key)
                            .font(.title2)
                            .frame(maxWidth: .infinity, minHeight: 60)
                            .background(key.isEmpty ? Color.clear : MaayaTheme.surface)
                            .clipShape(Circle())
                    }
                    .disabled(key.isEmpty || loading)
                    .foregroundStyle(.primary)
                }
            }

            Text("TRUSTED NETWORK · PIN AUTH")
                .font(.caption2)
                .tracking(2)
                .foregroundStyle(.secondary)
        }
    }

    private func press(_ key: String) {
        guard !loading, !key.isEmpty else { return }
        error = nil
        if key == "⌫" {
            if !digits.isEmpty { digits.removeLast() }
            return
        }
        guard digits.count < length else { return }
        digits.append(key)
        if digits.count == length {
            let pin = digits
            Task { await submit(pin) }
        }
    }

    private func submit(_ pin: String) async {
        loading = true
        defer { loading = false }
        do {
            try await auth.pinLogin(pin)
        } catch {
            digits = ""
            self.error = error.localizedDescription
            shake = true
            try? await Task.sleep(for: .milliseconds(500))
            shake = false
        }
    }
}

private struct CredentialsForm: View {
    let auth: AuthService

    @State private var username = ""
    @State private var password = ""
    @State private var error: String?
    @State private var loading = false

    var body: some View {
        VStack(spacing: 16) {
            if let error {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            TextField("Username", text: $username)
                .textContentType(.username)
                .autocorrectionDisabled()
                .textInputAutocapitalization(.never)
                .padding()
                .background(MaayaTheme.surface, in: RoundedRectangle(cornerRadius: 10))

            SecureField("Password", text: $password)
                .textContentType(.password)
                .padding()
                .background(MaayaTheme.surface, in: RoundedRectangle(cornerRadius: 10))

            Button {
                Task { await submit() }
            } label: {
                HStack {
                    if loading { ProgressView().tint(.black) }
                    Text(loading ? "Authenticating…" : "Initialize")
                        .fontWeight(.semibold)
                }
                .frame(maxWidth: .infinity)
                .padding()
                .background(MaayaTheme.gold)
                .foregroundStyle(.black)
                .clipShape(RoundedRectangle(cornerRadius: 10))
            }
            .disabled(loading || username.isEmpty || password.isEmpty)

            Text("REMOTE ACCESS · CREDENTIALS REQUIRED")
                .font(.caption2)
                .tracking(2)
                .foregroundStyle(.secondary)
                .padding(.top, 4)
        }
    }

    private func submit() async {
        loading = true
        defer { loading = false }
        error = nil
        do {
            try await auth.login(username: username, password: password)
        } catch {
            self.error = error.localizedDescription
        }
    }
}
