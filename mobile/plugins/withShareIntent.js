const { withAndroidManifest, withMainActivity } = require('expo/config-plugins');
const { mergeContents } = require('@expo/config-plugins/build/utils/generateCode');

// Kept identical to importar.tsx's ACCEPTED_TYPES so Fino only shows up in
// the Compartir sheet for files it can actually import.
const SHARE_MIME_TYPES = [
  'text/csv',
  'text/comma-separated-values',
  'application/vnd.ms-excel',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'text/plain',
];

const TAG = 'fino-share-intent';

/**
 * Local config plugin (no extra npm dependency -- `expo/config-plugins` is
 * part of the `expo` package itself) that makes Fino appear in Android's
 * "Compartir" sheet for bank-statement-shaped files, and turns that into the
 * exact same `fino:///compartir` deep link the iOS share extension
 * (targets/share/ShareViewController.swift) already opens. Android needs no
 * App Group or separate extension process for this -- MainActivity itself
 * receives the ACTION_SEND intent directly, so all this does is copy the
 * shared file into the app's own storage and rewrite the intent into a
 * normal VIEW deep link that expo-router's own Linking integration (already
 * registered for the "fino" scheme) picks up with no further plumbing.
 */
function withShareIntent(config) {
  config = withShareIntentFilter(config);
  config = withShareIntentMainActivity(config);
  return config;
}

function withShareIntentFilter(config) {
  return withAndroidManifest(config, (config) => {
    const application = config.modResults.manifest.application?.[0];
    const mainActivity = application?.activity?.find(
      (activity) => activity.$['android:name'] === '.MainActivity',
    );
    if (!mainActivity) {
      return config;
    }

    mainActivity['intent-filter'] = mainActivity['intent-filter'] ?? [];

    const alreadyPresent = mainActivity['intent-filter'].some((filter) =>
      (filter.action ?? []).some(
        (action) => action.$['android:name'] === 'android.intent.action.SEND',
      ),
    );
    if (alreadyPresent) {
      return config;
    }

    mainActivity['intent-filter'].push({
      action: [{ $: { 'android:name': 'android.intent.action.SEND' } }],
      category: [{ $: { 'android:name': 'android.intent.category.DEFAULT' } }],
      data: SHARE_MIME_TYPES.map((mimeType) => ({ $: { 'android:mimeType': mimeType } })),
    });

    return config;
  });
}

function withShareIntentMainActivity(config) {
  return withMainActivity(config, (config) => {
    config.modResults.contents = mergeContents({
      src: config.modResults.contents,
      newSrc: [
        'import android.content.Intent',
        'import android.net.Uri',
        'import android.provider.OpenableColumns',
        'import java.io.File',
        'import java.io.FileOutputStream',
      ].join('\n'),
      tag: `${TAG}-imports`,
      anchor: /^package app\.fino\.mobile$/m,
      offset: 1,
      comment: '//',
    }).contents;

    config.modResults.contents = mergeContents({
      src: config.modResults.contents,
      newSrc: '    intent = rewriteShareIntent(intent) ?: intent',
      tag: `${TAG}-oncreate`,
      anchor: /setTheme\(R\.style\.AppTheme\);/,
      offset: 1,
      comment: '//',
    }).contents;

    config.modResults.contents = mergeContents({
      src: config.modResults.contents,
      newSrc: [
        '  override fun onNewIntent(intent: Intent) {',
        '    super.onNewIntent(rewriteShareIntent(intent) ?: intent)',
        '  }',
        '',
        '  /**',
        "   * Fino's Android entry point for appearing in the Compartir sheet: this",
        '   * activity (singleTask) gets relaunched with a plain ACTION_SEND intent the',
        "   * same way any bank app's own \"share extracto\" button would launch it.",
        '   * There is no extension/App-Group boundary to cross on Android -- MainActivity',
        '   * runs in the same process, so the shared bytes go straight into the app\'s own',
        '   * private storage, and the intent is rewritten into the same fino:///compartir',
        "   * deep link the scheme's VIEW/BROWSABLE intent-filter above already handles,",
        "   * so it reaches app/compartir.tsx through expo-router's normal Linking flow.",
        '   */',
        '  private fun rewriteShareIntent(source: Intent?): Intent? {',
        '    if (source?.action != Intent.ACTION_SEND) {',
        '      return null',
        '    }',
        '    @Suppress("DEPRECATION")',
        '    val uri = source.getParcelableExtra<Uri>(Intent.EXTRA_STREAM) ?: return null',
        '',
        '    val fileName = queryDisplayName(uri) ?: uri.lastPathSegment ?: "compartido"',
        '    val inbox = File(filesDir, "share-inbox")',
        '    inbox.mkdirs()',
        '    val destination = File(inbox, fileName)',
        '',
        '    try {',
        '      val input = contentResolver.openInputStream(uri) ?: return null',
        '      input.use { stream ->',
        '        FileOutputStream(destination).use { output -> stream.copyTo(output) }',
        '      }',
        '    } catch (error: Exception) {',
        '      return null',
        '    }',
        '',
        '    val deepLink = Uri.parse("fino:///compartir?file=" + Uri.encode(fileName))',
        '    return Intent(Intent.ACTION_VIEW, deepLink)',
        '  }',
        '',
        '  private fun queryDisplayName(uri: Uri): String? {',
        '    return try {',
        '      contentResolver.query(uri, null, null, null, null)?.use { cursor ->',
        '        val index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)',
        '        if (index >= 0 && cursor.moveToFirst()) cursor.getString(index) else null',
        '      }',
        '    } catch (error: Exception) {',
        '      null',
        '    }',
        '  }',
        '',
      ].join('\n'),
      tag: `${TAG}-methods`,
      anchor: /class MainActivity ?: ?ReactActivity\(\) ?\{/,
      offset: 1,
      comment: '//',
    }).contents;

    return config;
  });
}

module.exports = withShareIntent;
