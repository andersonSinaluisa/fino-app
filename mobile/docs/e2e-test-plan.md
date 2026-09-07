# Mobile E2E Test Plan

Source: Expo EAS Workflows example for Maestro E2E tests.

## Goal

Validate that the Nexo mobile app can be built for device testing and that the critical anonymous entry path works before adding credential-dependent authenticated flows.

## Environment

- Project root for EAS and Maestro: `mobile`
- App id: `app.nexo.mobile`
- EAS build profile: `e2e-test`
- Runtime API for cloud E2E: `EXPO_PUBLIC_API_URL=https://api.staging.nexo.app`
- Credentials: intentionally omitted. Authenticated tests require seeded test users provided by environment variables or a staging fixture API.

## Test Matrix

| ID | Flow | Platform | Purpose | Credential dependency |
| --- | --- | --- | --- | --- |
| E2E-001 | `.maestro/auth-smoke.yml` | Android/iOS | Cold start, anonymous login screen, navigation to register and back | No |
| E2E-002 | `.maestro/register-validation.yml` | Android/iOS | Register screen input handling and short-password validation | No |
| E2E-001L | `.maestro/expo-go-auth-smoke.yml` | Android local | Same smoke path through Expo Go when a standalone APK cannot be installed locally | No |
| E2E-002L | `.maestro/expo-go-register-validation.yml` | Android local | Same register validation through Expo Go when a standalone APK cannot be installed locally | No |
| E2E-003 | Authenticated dashboard smoke | Android/iOS | Login, tabs, summary, accounts, transactions, profile | Yes, pending seeded user |
| E2E-004 | Import statement happy path | Android/iOS | Pick file, preview, confirm import | Yes, pending fixture file/user |
| E2E-005 | Notification/preferences smoke | Android/iOS | Preferences screen renders and toggles persist | Yes, pending seeded device state |

## Execution

Local optional execution, after installing Maestro and a built app on an emulator/simulator:

```sh
maestro test .maestro/auth-smoke.yml
maestro test .maestro/register-validation.yml
```

Local Expo Go fallback, useful when the emulator cannot install the EAS APK:

```sh
npx expo start --clear --host lan --port 8082
maestro test .maestro/expo-go-auth-smoke.yml
maestro test .maestro/expo-go-register-validation.yml
```

EAS Workflows execution:

```sh
eas workflow:run .eas/workflows/e2e-test-android.yml --non-interactive --wait
eas workflow:run .eas/workflows/e2e-test-ios.yml --non-interactive --wait
```

## Acceptance Criteria

- `npx expo-doctor` passes before EAS execution.
- Android workflow builds an APK with `e2e-test` and runs both Maestro flows.
- iOS workflow builds a simulator app with `e2e-test` and runs both Maestro flows.
- No secrets are committed to the repository.
- Authenticated flows stay blocked until staging credentials or seeded fixtures are available through EAS environment variables.
