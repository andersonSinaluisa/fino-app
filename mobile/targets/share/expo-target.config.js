/** @type {import('@bacons/apple-targets/app.plugin').ConfigFunction} */
module.exports = (config) => ({
  type: "share",
  name: "FinoShare",
  displayName: "Fino",
  // Same App Group the main app declares in app.json's ios.entitlements --
  // ShareViewController.swift writes the shared file's path here and the
  // main app (lib/shareImport.ts) reads it back the same way the widgets
  // read their snapshot (see lib/widgets/sync.ios.ts).
  entitlements: {
    "com.apple.security.application-groups":
      config.ios?.entitlements?.["com.apple.security.application-groups"] ?? [],
  },
  // Plain UIKit only -- no Social/MobileCoreServices/UniformTypeIdentifiers,
  // since ShareViewController is a bare UIViewController, not the
  // SLComposeServiceViewController the create-target scaffold started from.
  frameworks: ["UIKit"],
});
