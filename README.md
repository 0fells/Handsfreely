<p align="center">
  <img src="windows/Handsfreely.App/Assets/Square150x150Logo.scale-200.png" width="120" alt="Handsfreely">
</p>

<h1 align="center">Handsfreely</h1>

<p align="center">
  Звонки, контакты и SMS телефона Android на компьютере с Windows по Bluetooth.<br>
  Calls, contacts and SMS of an Android phone on a Windows PC over Bluetooth.
</p>

<p align="center">
  <a href="#русский">Русский</a> | <a href="#english">English</a>
</p>

# Русский

## Что умеет

- Звонить с компьютера и принимать звонки: звук разговора идёт через микрофон и динамики ПК (Bluetooth HFP).
- Звонок идёт в стандартном окне звонка Windows, а панель Handsfreely добавляет фото контакта, микрофон, удержание, тоновый набор и перевод звука на телефон.
- Контакты телефона с поиском и фотографиями, журнал звонков.
- SMS: переписки, ответ, новое сообщение, быстрый ответ прямо из уведомления. SMS идут по стандартному Bluetooth MAP, поэтому приложению на телефоне не нужен доступ к сообщениям и оно ставится без блокировок Play Защиты.
- Ссылки `tel:` в браузере открывают набор номера.
- Сопряжение по QR-коду и шифрование всего канала. Несколько телефонов на одном ПК и несколько ПК у одного телефона; у каждого пользователя Windows свои телефоны.
- Интерфейс на русском и английском.

Работает без интернета и без учётных записей: всё идёт напрямую между телефоном и компьютером.

## Требования

- Windows 10 версии 2004 или новее, либо Windows 11, с Bluetooth.
- Телефон на Android 10 или новее.
- На Windows 10 удержание, тоновый набор и перевод звука работают, только если телефон даёт приложению полное управление звонком. На Windows 11 их выполняет сама Windows.

## Как подключить

1. Сопрягите телефон с компьютером в параметрах Bluetooth Windows.
2. Установите Handsfreely на компьютер и на телефон.
3. На компьютере откройте "Телефоны" и нажмите "Добавить телефон".
4. На телефоне нажмите "Подключить ПК" и отсканируйте QR-код.
5. Разрешите в приложении на телефоне доступ к звонкам, контактам и журналу: что не разрешите, то не будет видно на ПК.
6. Когда телефон спросит, можно ли компьютеру получить доступ к сообщениям, разрешите: так работают SMS.

## Как это устроено

Звук разговора переносит стандартный профиль Bluetooth HFP: компьютер становится гарнитурой телефона. SMS компьютер читает и отправляет сам по стандартному профилю Bluetooth MAP. Контакты, журнал и управление звонком идут по отдельному каналу RFCOMM между программой на ПК и приложением на телефоне. Канал зашифрован (ECDH P-256, AES-256-GCM), а ключи сторон закрепляются при сканировании QR-кода. Протокол описан в [docs/protocol.md](docs/protocol.md).

## Сборка

Windows (нужен .NET SDK 10 и режим разработчика для отладочной установки):

```
dotnet test windows/Handsfreely.Core.Tests
dotnet build windows/Handsfreely.App -p:Platform=x64
```

Android (нужны JDK 17+ и Android SDK 35):

```
cd android
./gradlew testDebugUnitTest assembleDebug
```

## Лицензия

[MIT](LICENSE). Сторонние компоненты перечислены в [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

# English

## Features

- Place and take calls on the computer: call audio goes through the PC microphone and speakers (Bluetooth HFP).
- Calls run in the standard Windows call window, and the Handsfreely call panel adds the contact photo, mute, hold, keypad tones and moving audio to the phone.
- Phone contacts with search and photos, the call log.
- SMS: conversations, replies, new messages, quick reply right from the notification. SMS use standard Bluetooth MAP, so the phone app needs no message access and installs without Play Protect blocks.
- `tel:` links in the browser open the dialer.
- QR code pairing and an encrypted channel. Several phones per PC and several PCs per phone; each Windows user has their own phones.
- Russian and English interface.

Works without internet or accounts: everything goes directly between the phone and the computer.

## Requirements

- Windows 10 version 2004 or later, or Windows 11, with Bluetooth.
- A phone with Android 10 or later.
- On Windows 10, hold, keypad tones and moving audio work only when the phone grants the app full call control. On Windows 11, Windows does them itself.

## Getting connected

1. Pair the phone with the computer in Windows Bluetooth settings.
2. Install Handsfreely on the computer and on the phone.
3. On the computer, open "Phones" and click "Add a phone".
4. On the phone, tap "Connect a PC" and scan the QR code.
5. In the phone app, allow access to calls, contacts and the call log: whatever you don't allow won't show up on the PC.
6. When the phone asks whether the computer may access messages, allow it: this is how SMS work.

## How it works

Call audio uses the standard Bluetooth HFP profile: the computer becomes the phone's headset. The computer reads and sends SMS itself over the standard Bluetooth MAP profile. Contacts, the call log and call control travel over a separate RFCOMM channel between the PC program and the phone app. The channel is encrypted (ECDH P-256, AES-256-GCM), and the keys of both sides are pinned when the QR code is scanned. The protocol is described in [docs/protocol.en.md](docs/protocol.en.md).

## Building

Windows (needs the .NET 10 SDK and Developer Mode for a debug install):

```
dotnet test windows/Handsfreely.Core.Tests
dotnet build windows/Handsfreely.App -p:Platform=x64
```

Android (needs JDK 17+ and Android SDK 35):

```
cd android
./gradlew testDebugUnitTest assembleDebug
```

## License

[MIT](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
