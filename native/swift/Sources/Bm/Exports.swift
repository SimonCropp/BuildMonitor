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

@_cdecl("bm_init")
public func bmInit(
    _ width: Int32,
    _ height: Int32,
    _ title: UnsafePointer<CChar>?,
    _ fontTtf: UnsafePointer<UInt8>?,
    _ fontLength: Int32,
    _ fontSize: Float,
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
        hidden: hidden != 0)
    return opened ? 1 : 0
}

@_cdecl("bm_present")
public func bmPresent(_ screen: UnsafePointer<BmScreen>?) -> Int32 {
    let runtime = Runtime.shared
    guard runtime.initialised, let screen else {
        return 0
    }

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
    context.translateBy(x: 0, y: CGFloat(height))
    context.scaleBy(x: 1, y: -1)
    renderer.draw(Frame.decode(screen), in: context, size: CGSize(width: CGFloat(width), height: CGFloat(height)), staticForm: true)

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
