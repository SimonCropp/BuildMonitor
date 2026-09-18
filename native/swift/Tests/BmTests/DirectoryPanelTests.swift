import AppKit
import CoreGraphics
import XCTest
@testable import Bm

/// The panel the code directory is chosen with. AppKit, so a window server has to be there: these
/// make a panel and look at it, and only the last one puts it up.
final class DirectoryPanelTests: XCTestCase {
    override func setUpWithError() throws {
        try super.setUpWithError()
        // Asked before AppKit is touched rather than after: there is no window server over ssh or
        // on a machine with nobody logged in, and NSApplication takes the process down with it
        // rather than failing, which would read as this suite being broken.
        try XCTSkipIf(
            CGSessionCopyCurrentDictionary() == nil,
            "No window server session to make a panel in.")
        // An NSOpenPanel is a window, and a window needs the application it belongs to. Nothing is
        // run: this only brings NSApp into being.
        _ = NSApplication.shared
    }

    func testItChoosesFoldersRatherThanFiles() {
        let panel = Runtime.shared.directoryPanel(start: nil)

        XCTAssertTrue(panel.canChooseDirectories)
        XCTAssertFalse(panel.canChooseFiles)
        XCTAssertFalse(panel.allowsMultipleSelection)
    }

    func testItOpensAtTheDirectoryAlreadySet() {
        let home = NSHomeDirectory()
        let panel = Runtime.shared.directoryPanel(start: home)

        // Symlinks resolved on both sides: /var is one, and a panel handed a path through it does
        // not have to give the same one back.
        XCTAssertEqual(
            panel.directoryURL?.resolvingSymlinksInPath().path,
            URL(fileURLWithPath: home).resolvingSymlinksInPath().path)
    }

    /// A code directory that has since been moved or deleted. Opening the panel at a path that is
    /// not there shows the user an empty column and no reason for it, so it is left to open
    /// wherever it last did.
    func testItDoesNotOpenAtADirectoryThatIsGone() {
        let panel = Runtime.shared.directoryPanel(start: "/nowhere/in/particular")

        XCTAssertNotEqual(panel.directoryURL?.path, "/nowhere/in/particular")
    }

    func testAPanelThatWasCancelledPicksNothing() {
        let picked = Runtime.shared.pickDirectory(start: nil) { (_: NSOpenPanel) -> URL? in nil }

        XCTAssertNil(picked)
    }

    func testTheDirectoryThePanelWasAnsweredWithComesBack() {
        let chosen = URL(fileURLWithPath: NSHomeDirectory())
        let picked = Runtime.shared.pickDirectory(start: nil) { _ in chosen }

        XCTAssertEqual(picked, chosen.path)
    }

    /// The real panel, put up and then taken away again from a timer. This is the only test that
    /// reaches `runModal`, which is the whole point of the design: it is what pumps AppKit while
    /// the panel is up, and pumping is what stops the window behind it beachballing.
    ///
    /// Off unless asked for. A panel that does not come down hangs the run rather than failing it,
    /// and a machine with no window server has nothing to put one on.
    func testTheRealPanelIsModalAndComesDownWhenCancelled() throws {
        try XCTSkipUnless(
            ProcessInfo.processInfo.environment["BUILDMONITOR_PANEL_TESTS"] == "true",
            "Set BUILDMONITOR_PANEL_TESTS=true to put a real panel up.")

        // Fired while the panel is up, because a modal session runs the loop in .modalPanel, which
        // is one of the common modes. Repeating and asked rather than the once: aborting when
        // nothing is modal throws, and a cold machine can take longer to put a panel up than any
        // one delay would have allowed for.
        let cancelling = Timer(timeInterval: 0.5, repeats: true) { timer in
            guard NSApp.modalWindow != nil else {
                return
            }

            NSApp.abortModal()
            timer.invalidate()
        }
        RunLoop.main.add(cancelling, forMode: .common)
        defer { cancelling.invalidate() }

        XCTAssertNil(Runtime.shared.pickDirectory(start: nil))
    }
}
