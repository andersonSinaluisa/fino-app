const appJson = require('./app.json');

const APPLE_TARGETS_PLUGIN = '@bacons/apple-targets';

function withAppleTeamId(config, appleTeamId) {
  if (!appleTeamId) {
    return config;
  }

  return {
    ...config,
    ios: {
      ...config.ios,
      appleTeamId,
    },
    plugins: (config.plugins ?? []).map((plugin) => {
      if (plugin === APPLE_TARGETS_PLUGIN) {
        return [APPLE_TARGETS_PLUGIN, { appleTeamId }];
      }

      if (Array.isArray(plugin) && plugin[0] === APPLE_TARGETS_PLUGIN) {
        return [APPLE_TARGETS_PLUGIN, { ...(plugin[1] ?? {}), appleTeamId }];
      }

      return plugin;
    }),
  };
}

module.exports = () => {
  const config = JSON.parse(JSON.stringify(appJson.expo));
  return withAppleTeamId(config, process.env.EXPO_APPLE_TEAM_ID);
};
