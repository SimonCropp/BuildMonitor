import XCTest
@testable import Bm

/// What bm_pick_directory hands back across the ABI. No AppKit, so these run wherever the package
/// builds, with or without a window server to put a panel on.
final class PickedPathTests: XCTestCase {
    func testTheChosenPathIsWrittenAsUtf8() {
        var buffer = [UInt8](repeating: 0, count: 64)
        let written = buffer.withUnsafeMutableBufferPointer {
            writePicked("/Users/someone/code", into: $0.baseAddress!, length: Int32($0.count))
        }

        XCTAssertEqual(written, 19)
        XCTAssertEqual(String(bytes: buffer[0 ..< Int(written)], encoding: .utf8), "/Users/someone/code")
    }

    /// The length is the answer, and the managed side reads exactly that many bytes back. Counting
    /// characters instead would cut the last byte off any path with anything outside ASCII in it,
    /// leaving a code directory that matches no checkout and a Browse that looks broken.
    func testAPathIsMeasuredInBytesRatherThanCharacters() {
        // Escaped rather than typed, so what this measures is UTF-8 rather than however an editor
        // decided to normalise the file.
        let path = "/Users/someone/k\u{00F3}d"
        var buffer = [UInt8](repeating: 0, count: 64)
        let written = buffer.withUnsafeMutableBufferPointer {
            writePicked(path, into: $0.baseAddress!, length: Int32($0.count))
        }

        XCTAssertEqual(path.count, 18)
        XCTAssertEqual(written, 19)
        XCTAssertEqual(String(bytes: buffer[0 ..< Int(written)], encoding: .utf8), path)
    }

    /// Half a path would be saved as the code directory and match nothing, so it is reported as a
    /// cancel instead, which leaves the setting as it was.
    func testAPathThatDoesNotFitIsNotCutShort() {
        var buffer = [UInt8](repeating: 0, count: 8)
        let written = buffer.withUnsafeMutableBufferPointer {
            writePicked("/Users/someone/code", into: $0.baseAddress!, length: Int32($0.count))
        }

        XCTAssertEqual(written, 0)
        XCTAssertEqual(buffer, [UInt8](repeating: 0, count: 8))
    }

    func testACancelWritesNothing() {
        var buffer = [UInt8](repeating: 0, count: 8)
        let written = buffer.withUnsafeMutableBufferPointer {
            writePicked(nil, into: $0.baseAddress!, length: Int32($0.count))
        }

        XCTAssertEqual(written, 0)
        XCTAssertEqual(buffer, [UInt8](repeating: 0, count: 8))
    }

    /// Nowhere to put a path is the question rather than the request, which is how a test finds out
    /// that this head has a panel without one opening. -1 would be the answer from a head that has
    /// none, and would send the managed side off looking for zenity on a Mac.
    func testAskingWithNowhereToPutTheAnswerOpensNothing() {
        XCTAssertEqual(bmPickDirectory(nil, nil, 0), 0)
    }
}
