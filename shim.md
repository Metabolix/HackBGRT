# Secure Boot and *shim*

Secure Boot accepts only trusted files during boot. The *shim* boot loader is a tool which allows you to select which files to trust. HackBGRT installs *shim* for you, but you need to configure it with these instructions.

On the first boot after installing HackBGRT, you will see an error message saying "Verification failed". You need to press a key to enter the setup tool (MOKManager) where you can choose to trust HackBGRT. After that, use arrow keys to navigate and *Enter* to continue as described below.

## 1. Verification failed

This is the start of *shim* configuration.

```
ERROR
Verification failed: (0x1A) Security Violation
+----+
| OK |
+----+
```

Select `OK`, *Enter*.

```
Shim UEFI key management
Press any key to perform MOK management
Booting in 5 seconds
```

Press a key quickly to enter *MOK management* or the *MOKManager* program.

## 2. MOK management

```
Perform MOK management

Continue to boot
Enroll key from disk
Enroll hash from disk
```

Select `Enroll hash from disk`, *Enter*. This is the safest option where you choose to trust only a specific version of HackBGRT.

You can also choose to `Enroll key from disk`, which means that you choose to trust anything signed with the same certificate. How do you know if it's safe? You don't – that's why you should rather use the other option or build your own version of HackBGRT with your own certificate.

## 3a. Enroll hash from disk

```
Select Binary

The Selected Binary will have its hash Enrolled
This means it will subsequently Boot with no prompting
Remember to make sure it is a genuine binary before enrolling its hash

+----------------+
| YOUR DISK NAME |
+----------------+
```

Select the disk, *Enter*.

```
+---------------+
|     EFI/      |
|    loader/    |
| vmlinuz-linux |
+---------------+
```

Select `EFI/`, *Enter*.

```
+------------+
|    ../     |
|   Boot/    |
| HackBGRT/  |
| Microsoft/ |
+------------+
```

Select `HackBGRT/`, *Enter*.

```
+-----------------+
|       ../       |
|   grubx64.efi   |
|   loader.efi    |
|    mmx64.efi    |
| certificate.cer |
|   splash.bmp    |
|   config.txt    |
+-----------------+
```

Select `grubx64.efi`, *Enter*.

```
[Enroll MOK]

+------------+
| View key 0 |
|  Continue  |
+------------+
```

To verify the key contents, select `View key 0`, *Enter*.

```
SHA256 hash
(some hexadecimal values)
```

Press *Enter* to continue.

```
[Enroll MOK]

+------------+
| View key 0 |
|  Continue  |
+------------+
```

Select `Continue`, *Enter*.

```
Enroll the key(s)?

+-----+
| No  |
| Yes |
+-----+
```

Select `Yes`, *Enter*.

```
Perform MOK management

+-----------------------+
|        Reboot         |
| Enroll key from disk  |
| Enroll hash from disk |
+-----------------------+
```

Select `Reboot`, *Enter*.

You are now ready to boot using HackBGRT.

## 3b. Enroll key from disk

```
Select Key

The selected key will be enrolled into the MOK database
This means any binaries signed with it will be run without prompting
Remember to make sure it is a genuine key before Enrolling it

+----------------+
| YOUR DISK NAME |
+----------------+
```

Select the disk, *Enter*.

```
+---------------+
|     EFI/      |
|    loader/    |
| vmlinuz-linux |
+---------------+
```

Select `EFI/`, *Enter*.

```
+------------+
|    ../     |
|   Boot/    |
| HackBGRT/  |
| Microsoft/ |
+------------+
```

Select `HackBGRT/`, *Enter*.

```
+-----------------+
|       ../       |
|   grubx64.efi   |
|   loader.efi    |
|    mmx64.efi    |
| certificate.cer |
|   splash.bmp    |
|   config.txt    |
+-----------------+
```

Select `certificate.cer`, *Enter*.

```
[Enroll MOK]

+------------+
| View key 0 |
|  Continue  |
+------------+
```

To verify the key contents, select `View key 0`, *Enter*.

```
[Extended Key Usage]
OID: Code Signing

[Serial Number]
6B:24:52:E9:3B:84:41:73:B0:22:92:E8:BE:8E:38:85:

[Issuer]
CN=HackBGRT Secure Boot Signer, O=Metabolix

[Subject]
CN=HackBGRT Secure Boot Signer, O=Metabolix

[Valid Not Before]
Nov  9 13:43:56 2023 GMT

[Valid Not After]
Jan 19 03:14:07 2037 GMT

[Fingerprint]
79 8E 64 40 D1 D1 F4 53 30 8D
A0 83 A4 77 FE 57 45 30 36 60
```

Press *Enter* to continue.

```
[Enroll MOK]

+------------+
| View key 0 |
|  Continue  |
+------------+
```

Select `Continue`, *Enter*.

```
Enroll the key(s)?

+-----+
| No  |
| Yes |
+-----+
```

Select `Yes`, *Enter*.

```
Perform MOK management

+-----------------------+
|        Reboot         |
| Enroll key from disk  |
| Enroll hash from disk |
+-----------------------+
```

Select `Reboot`, *Enter*.

You are now ready to boot using HackBGRT.

## Tutorial: *shim* for dummies

To install *shim* manually, follow these steps (assuming x64 architecture):

1. Get *shim*, preferably *shim-signed*.
2. Rename your boot loader to `grubx64.efi`.
3. Copy `shimx64.efi` where your loader used to be.
4. Copy `mmx64.efi` to the same folder.

The *shim* boot process is as follows:

1. Your computer starts `your-loader-name.efi`, which is now really *shim*.
2. Next, *shim* tries to load `grubx64.efi`.
3. If `grubx64.efi` is trusted, the boot process continues.
4. Otherwise, *shim* offers to launch *MOKManager* `mmx64.efi`, and you can try again after that.
