import SwiftUI

// Probe → PIN pad on a trusted mesh network, else username/password.
// Mirrors the web frontend's Login.tsx / PinPad.tsx behavior.
struct LoginView: View {
    let auth: AuthService

    @State private var probing = true
    @State private var method: AuthMethod = .credentials
    @State private var forceCredentials = false

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
        }
        .task { await runProbe() }
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
