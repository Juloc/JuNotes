import JuNotesCore
import SwiftUI
import VisionKit

/// The second way in: a code from the web page (Einstellungen, Gerät koppeln), typed, pasted, or read from its QR code. The QR code holds the address of the server and the
/// code (`<server>/#pair=<CODE>`), never a key; when its server is not the one typed in the settings, the app asks first.
struct PairingCodeView: View {
    let server: URL?
    let connect: @MainActor (String, URL) async throws -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var typed = ""
    @State private var scanning = false
    @State private var working = false
    @State private var message: String?
    @State private var otherServer: (server: URL, code: String)?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("ABCDE-FGHIJ", text: $typed)
                        .font(.title2.monospaced())
                        .textInputAutocapitalization(.characters).autocorrectionDisabled()
                        .onChange(of: typed) { _, text in readPasted(text) }
                    if DataScannerViewController.isSupported {
                        Button("QR-Code scannen", systemImage: "qrcode.viewfinder") { scanning = true }
                    }
                } header: {
                    Text("Code")
                } footer: {
                    Text("Den Code bekommst du im Browser unter Einstellungen, Gerät koppeln. Er gilt zehn Minuten und nur einmal.")
                }

                Section {
                    Button { Task { await connectTyped() } } label: {
                        HStack { Text("Verbinden"); if working { Spacer(); ProgressView() } }
                    }
                    .disabled(working || Pairing.normalise(typed) == nil || server == nil)
                    if server == nil { Text("Trag zuerst die Adresse des Servers ein, oder scanne den QR-Code.").font(.footnote).foregroundStyle(.secondary) }
                    if let message { Text(message).font(.footnote).foregroundStyle(.red) }
                }
            }
            .navigationTitle("Mit Code verbinden")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } } }
            .fullScreenCover(isPresented: $scanning) {
                QRScannerView { text in
                    scanning = false
                    readScanned(text)
                }
                .ignoresSafeArea()
                .overlay(alignment: .topTrailing) {
                    Button("Abbrechen") { scanning = false }.buttonStyle(.borderedProminent).padding(24)
                }
            }
            .confirmationDialog("Mit diesem Server verbinden?", isPresented: Binding(get: { otherServer != nil }, set: { if !$0 { otherServer = nil } }), titleVisibility: .visible) {
                if let other = otherServer {
                    Button("Mit \(other.server.host() ?? other.server.absoluteString) verbinden") { Task { await run(other.code, other.server) } }
                }
                Button("Abbrechen", role: .cancel) { otherServer = nil }
            } message: {
                Text(server == nil ? "Der QR-Code nennt diesen Server. Deine Hefte werden dorthin gesichert." : "Der QR-Code gehört zu einem anderen Server als dem eingetragenen. Deine Hefte würden dorthin gesichert.")
            }
        }
    }

    /// A pasted link from the web page is read like a scanned QR code.
    private func readPasted(_ text: String) {
        guard text.contains("#pair=") else { return }
        typed = ""
        readScanned(text)
    }

    private func readScanned(_ text: String) {
        guard let link = Pairing.parseLink(text) else {
            message = "Das ist kein QR-Code zum Koppeln."
            return
        }
        typed = Pairing.formatted(link.code)
        if let server, server.host()?.lowercased() == link.server.host()?.lowercased() {
            Task { await run(link.code, server) }
        } else {
            // the server comes from the QR code (none typed, or another one): she says yes to it first, with its name, so nobody's code can send her notebooks elsewhere
            otherServer = link
        }
    }

    private func connectTyped() async {
        guard let server, let code = Pairing.normalise(typed) else { return }
        await run(code, server)
    }

    private func run(_ code: String, _ server: URL) async {
        otherServer = nil
        message = nil
        working = true
        defer { working = false }
        do {
            try await connect(code, server)
            dismiss()
        } catch let error as PairingError {
            message = Self.text(for: error)
        } catch let error as SignInError {
            message = error.text
        } catch {
            message = "Das hat nicht geklappt. Versuch es noch einmal."
        }
    }

    static func text(for error: PairingError) -> String {
        switch error {
        case .codeRefused: "Der Code stimmt nicht oder ist abgelaufen."
        case .noNetwork: "Kein Netz. Prüfe das WLAN."
        case .serverUnreachable: "Der Server antwortet nicht. Prüfe die Adresse."
        case .notSecure: "Verbinden geht nur über https."
        }
    }
}

/// The camera, reading one QR code. Only shown where the iPad can do it (`DataScannerViewController.isSupported`).
struct QRScannerView: UIViewControllerRepresentable {
    let found: @MainActor (String) -> Void

    func makeUIViewController(context: Context) -> DataScannerViewController {
        let scanner = DataScannerViewController(recognizedDataTypes: [.barcode(symbologies: [.qr])], qualityLevel: .balanced, recognizesMultipleItems: false, isHighFrameRateTrackingEnabled: false, isHighlightingEnabled: true)
        scanner.delegate = context.coordinator
        return scanner
    }

    func updateUIViewController(_ scanner: DataScannerViewController, context: Context) {
        if !scanner.isScanning { try? scanner.startScanning() }
    }

    func makeCoordinator() -> Coordinator { Coordinator(found: found) }

    @MainActor
    final class Coordinator: NSObject, DataScannerViewControllerDelegate {
        private let found: @MainActor (String) -> Void
        private var done = false

        init(found: @escaping @MainActor (String) -> Void) {
            self.found = found
        }

        func dataScanner(_ dataScanner: DataScannerViewController, didAdd addedItems: [RecognizedItem], allItems: [RecognizedItem]) {
            guard !done else { return }
            for item in addedItems {
                if case .barcode(let code) = item, let text = code.payloadStringValue {
                    done = true
                    dataScanner.stopScanning()
                    found(text)
                    return
                }
            }
        }
    }
}
