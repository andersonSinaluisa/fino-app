/** @type {import('@bacons/apple-targets/app.plugin').ConfigFunction} */
module.exports = (config) => ({
  type: "widget",
  name: "FinoWidgets",
  displayName: "Fino",
  colors: {
    // Matches the app's primary brand color (theme/tokens.ts colors.text) so
    // the widget-editing UI's accent tint doesn't look out of place.
    $accent: "#1D1D1B",
    // Apple's reserved key for the "add widget" gallery/configuration
    // background -- the app's actual paper background, not a system default.
    $widgetBackground: "#F5F3ED",
    // The rest mirror theme/tokens.ts colors.* 1:1 (kept as plain color
    // assets, referenced as Color("name") from the widget views) so the
    // widgets use exactly the same palette as the rest of the app instead of
    // system-default secondary gray and a plain system background.
    surface: "#FFFFFF",
    surfaceSecondary: "#ECE9E1",
    textPrimary: "#191A18",
    textSecondary: "#74766F",
    accentLime: "#C7F36B",
    accentMint: "#8DD9B6",
    onAccent: "#1D1D1B",
  },
  entitlements: {
    // Same App Group the main app declares in app.json's ios.entitlements --
    // this is how ExtensionStorage (UserDefaults-backed) is shared between
    // the app process and this widget extension process. Mirrored explicitly
    // (rather than relying on the "widget" target's appGroupsByDefault) so
    // the shared group id lives in one obvious place per platform.
    "com.apple.security.application-groups":
      config.ios?.entitlements?.["com.apple.security.application-groups"] ?? [],
  },
  // Frameworks beyond WidgetKit/SwiftUI/AppIntents (already added by default
  // for "widget" targets) aren't needed -- no ActivityKit (no Live Activity),
  // no Control Center widget in this feature.
  frameworks: ["WidgetKit", "SwiftUI", "AppIntents"],
});
