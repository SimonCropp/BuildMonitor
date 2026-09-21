import AppKit
import CBm
import CoreGraphics
import Foundation
import ImageIO

/// The entry points of native/include/bm.h, implemented over AppKit.
///
/// The header is imported for its struct layouts only, with BM_TYPES_ONLY, so these are the
/// definitions of those symbols rather than a second declaration of them.

@_cdecl("bm_version")
public func bmVersion() -> Int32 {
    Int32(BM_VERSION)
}

/// The emoji font is for a renderer with a glyph atlas of its own, and Core Text finds the mark by
/// itself, so it goes unused here. It is declared all the same: @_cdecl matches bm.h by position
/// alone, and without it hidden was read from the register the emoji pointer arrived in, which is
/// never zero, so a start that asked for the window never made one.
@_cdecl("bm_init")
public func bmInit(
    _ width: Int32,
    _ height: Int32,
    _ title: UnsafePointer<CChar>?,
    _ fontTtf: UnsafePointer<UInt8>?,
    _ fontLength: Int32,
    _ emojiTtf: UnsafePointer<UInt8>?,
    _ emojiLength: Int32,
    _ fontSize: Float,
    _ placement: UnsafePointer<BmPlacement>?,
    _ hidden: Int32) -> Int32 {
    var font: Data?
    if let fontTtf, fontLength > 0 {
        font = Data(bytes: fontTtf, count: Int(fontLength))
    }

    let opened = Runtime.shared.open(
        width: width,
        height: height,
        title: title.map { String(cString: $0) } ?? "BuildMonitor",
        font: font,
        fontSize: CGFloat(fontSize),
        placement: placement?.pointee,
        hidden: hidden != 0)
    return opened ? 1 : 0
}

@_cdecl("bm_present")
public func bmPresent(_ screen: UnsafePointer<BmScreen>?) -> Int32 {
    let runtime = Runtime.shared
    guard runtime.initialised, let screen else {
        return 0
    }

    // The managed side presents every frame, and a screen it has not rebuilt comes with the same
    // generation. Decoded and drawn anyway, every string was copied and every row drawn sixty times
    // a second to put the same pixels on screen.
    let generation = screen.pointee.generation
    if generation == runtime.presentedGeneration {
        runtime.pumpUnchanged()
        return 1
    }

    runtime.presentedGeneration = generation
    runtime.present(Frame.decode(screen))
    return 1
}

@_cdecl("bm_poll_input")
public func bmPollInput(_ input: UnsafeMutablePointer<BmInput>?) {
    guard let input else {
        return
    }

    let runtime = Runtime.shared
    if runtime.initialised {
        runtime.measure()
        runtime.drainEdit()
        runtime.samplePlacement()
    }

    input.pointee = runtime.input
    runtime.resetInput()
}

@_cdecl("bm_set_hidden")
public func bmSetHidden(_ hidden: Int32) {
    if hidden == 0 {
        Runtime.shared.show()
    } else {
        Runtime.shared.hide()
    }
}

@_cdecl("bm_focus")
public func bmFocus() {
    Runtime.shared.show()
}

@_cdecl("bm_set_clipboard")
public func bmSetClipboard(_ text: UnsafePointer<CChar>?) {
    guard let text else {
        return
    }

    let board = NSPasteboard.general
    board.clearContents()
    board.setString(String(cString: text), forType: .string)
}

@_cdecl("bm_pick_directory")
public func bmPickDirectory(
    _ start: UnsafePointer<CChar>?,
    _ buffer: UnsafeMutablePointer<UInt8>?,
    _ bufferLength: Int32) -> Int32 {
    // Nowhere to put a path is the question rather than the request: answered without a panel, so
    // that asking which head this is does not leave one open.
    guard let buffer, bufferLength > 0 else {
        return 0
    }

    let picked = Runtime.shared.pickDirectory(start: start.map { String(cString: $0) })
    return writePicked(picked, into: buffer, length: bufferLength)
}

/// The answer as the managed side reads it back: the path as UTF-8 with no terminator, and how many
/// bytes that was. Measured in bytes rather than characters, because a path with anything outside
/// ASCII in it is longer than it reads.
///
/// A path longer than the buffer cannot happen on this platform, and half a path is worse than
/// none: it would be saved as the code directory and match nothing. So it is reported as a cancel,
/// which leaves the setting as it was.
func writePicked(_ picked: String?, into buffer: UnsafeMutablePointer<UInt8>, length: Int32) -> Int32 {
    guard let picked else {
        return 0
    }

    let bytes = Array(picked.utf8)
    guard bytes.count <= Int(length) else {
        return 0
    }

    buffer.update(from: bytes, count: bytes.count)
    return Int32(bytes.count)
}

@_cdecl("bm_tray_available")
public func bmTrayAvailable() -> Int32 {
    1
}

@_cdecl("bm_tray_init")
public func bmTrayInit() -> Int32 {
    let runtime = Runtime.shared
    if runtime.tray != nil {
        return 1
    }

    // Accessory until a window is shown: a tray app should not put an empty app in the Dock.
    NSApplication.shared.setActivationPolicy(.accessory)
    let tray = Tray(runtime: runtime)
    guard tray.start() else {
        return 0
    }

    runtime.tray = tray
    return 1
}

@_cdecl("bm_tray_set_icon")
public func bmTraySetIcon(_ kind: Int32, _ png: UnsafePointer<UInt8>?, _ length: Int32) {
    guard let png, length > 0 else {
        return
    }

    Runtime.shared.tray?.setIcon(kind: kind, png: Data(bytes: png, count: Int(length)))
}

@_cdecl("bm_tray_set_menu_icon")
public func bmTraySetMenuIcon(_ name: UnsafePointer<CChar>?, _ png: UnsafePointer<UInt8>?, _ length: Int32) {
    guard let name, let png, length > 0 else {
        return
    }

    Runtime.shared.tray?.setMenuIcon(name: String(cString: name), png: Data(bytes: png, count: Int(length)))
}

@_cdecl("bm_set_row_icon")
public func bmSetRowIcon(_ name: UnsafePointer<CChar>?, _ png: UnsafePointer<UInt8>?, _ length: Int32) {
    guard let name, let png, length > 0,
          let image = NSImage(data: Data(bytes: png, count: Int(length))) else {
        return
    }

    image.size = NSSize(width: 16, height: 16)
    RowIcons.images[String(cString: name)] = image
}

@_cdecl("bm_shutdown")
public func bmShutdown() {
    Runtime.shared.shutdown()
}

/// Renders into a bitmap of this side's own making, with scale, colour space and font smoothing
/// pinned, so a committed baseline matches on any display. No window is needed.
@_cdecl("bm_capture")
public func bmCapture(
    _ screen: UnsafePointer<BmScreen>?,
    _ width: Int32,
    _ height: Int32,
    _ pngPath: UnsafePointer<CChar>?) -> Int32 {
    guard let screen,
          let pngPath,
          let renderer = Runtime.shared.renderer,
          let space = CGColorSpace(name: CGColorSpace.sRGB),
          let context = CGContext(
              data: nil,
              width: Int(width),
              height: Int(height),
              bitsPerComponent: 8,
              bytesPerRow: 0,
              space: space,
              bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
    else {
        return 0
    }

    context.setAllowsFontSmoothing(false)
    context.setShouldSmoothFonts(false)
    context.setAllowsFontSubpixelPositioning(false)
    context.setShouldSubpixelPositionFonts(false)
    context.setAllowsFontSubpixelQuantization(false)
    context.setShouldSubpixelQuantizeFonts(false)

    // Top-left origin like the view, so the capture and the window agree.
    _ = Palette.use(screen.pointee.theme)

    context.translateBy(x: 0, y: CGFloat(height))
    context.scaleBy(x: 1, y: -1)
    renderer.draw(Frame.decode(screen), in: context, size: CGSize(width: CGFloat(width), height: CGFloat(height)), staticControls: true)

    guard let image = context.makeImage() else {
        return 0
    }

    let url = URL(fileURLWithPath: String(cString: pngPath)) as CFURL
    guard let destination = CGImageDestinationCreateWithURL(url, "public.png" as CFString, 1, nil) else {
        return 0
    }

    CGImageDestinationAddImage(destination, image, nil)
    return CGImageDestinationFinalize(destination) ? 1 : 0
}
