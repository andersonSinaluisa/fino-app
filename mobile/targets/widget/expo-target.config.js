/** @type {import('@bacons/apple-targets/app.plugin').ConfigFunction} */
module.exports = (config) => ({
  type: "widget",
  name: "FinoWidgets",
  displayName: "Fino",
  colors: {
    // Matches the app's primary brand color (theme/tokens.ts colors.text) so
    // the widget-editing UI's accent tint doesn't look out of place.
    $accent: "#1D1D1B",
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
