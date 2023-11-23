EN | [中文](Tutorial_CN.md)

## Summary

1. [Introduction](#introduction)
2. [Why were we doing this?](#why-were-we-doing-this)
3. [How is UA Patch created?](#how-is-ua-patch-created)
4. [Afterwards - Usage](#afterwards---usage)
5. [Appendix: Timeline of 'Minor' Events](#appendix-timeline-of-minor-events)

## Introduction
   
As we all know, before the 3.2 version, if you want to play `the certain anime game` PS, you must patch `UserAssembly.dll`.

Because of the implemention of `HomoPro.sys`, UA Patch has become a thing of past, but in my opinion, it still worth some exploration.

As Ey Sey It, the greatest scientist in 21st century, said that,

> If there's something more evil than gatekeeping beta, that must be gatekeeping knowledge.

## Why were we doing this?

To understand this thing, we need to roughly know the process of communication between the client and the Dispatch server.

Dispatch is mainly doing two tasks: `query_region_list` and `query_cur_region`.

After login finished _(or before? It's not important though)_, the client sends `query_region_list` request to Dispatch, mainly use the version as a paramter.

Let's watch a `query_region_list` record example:

```http
GET http://dispatchcnglobal.circle.com/query_region_list
    ?version=CNCBWin3.2.50
    &lang=2
    &platform=3
    &binary=1
    &time=114
    &channel_id=1
    &sub_channel_id=1
    
Bytes Received:  ...		(headers:...; body:...)

Response Body:
EgkKB2Rldl9naW8...
```

`query_region_list` returns a base64 string, which mainly contains: 

- Available server list  
  Consists of the server's type, name, the `query_cur_region` url and other information.
  
- The whole `client_secret_key`  
  Usually starts with `Ec2b`, or we can say it's using Ec2b format. It can be used to generate `server_secret_key` (often called `dispatchKey`).
  
- `client_custom_config`, or the related config of client. Has been XORed with `dispatchKey`.

If there's more than one region, the client will list available servers.

After you have chosen one region, the client sends `query_cur_region` request to the dispatch url that the client has got from region info.

```http
GET http://cnbeta01dispatch.circle.com/query_cur_region
    ?version=CNCBWin3.2.50
    &lang=2
    &platform=3
    &binary=1
    &time=514
    &channel_id=1
    &sub_channel_id=1
    &account_type=1
    &dispatchSeed=kFccr4zydAy4Vm50
    
Bytes Received:  ...		(headers:...; body:...)

Response Body:
{
  "content": "i24XH9UKI1UdqdVW...",
  "sign": "wQ+sR16iZV3UMG..."
}
```

The response is in json format, containing `content` and `sign`, which are both encrypted with RSA keys.

To make the relationship between they and encryption clear, we might as well call:

- keys used to encrypt/decrypt `content` as `ClientPub, ClientPri`;
- keys used to encrypt/decrypt `sign` as `ServerPub, ServerPri`;

`content` is the Protobuf content that has been encrypted with the public key, and should be decrypted with the private key;

But `sign` goes to its opposite. It uses, more precisely, the private key to sign, and the client use the public key to verify the signature.

Since they are both encrypted by the server, the client can do nothing but decrypt them. So, the client does have 2 keys used for decryption `ClientPri, ServerPub`, hiding in our topic `UserAssembly.dll`.

Because the exponent is fixed (in fact provided), we can use the private key to generate public key, so `ClientPub` is also known to us.

Finally, we only leave `ServerPri` unknown. The anime game company reached its goal: `sign` is originally created for verifying the server. Without private key, there's no way to generate a proper signature.

> Actually, the earlist `query_cur_region` only returns a base64 string (raw Protobuf data), just like `query_region_list`. You can refer to [Dispatch implementation at that time](https://github.com/Grasscutters/Grasscutter/blob/41365c38d7bf143f7263b90e891401ae820320d9/src/main/java/emu/grasscutter/server/dispatch/DispatchServer.java#L134).

> This change was first noticed when the 2.7.5 (or 2.8 Beta) was running, in which version the anime game company implemented various protections to avoid PS from expanding, not only dispatch but also encryption of KCP traffic and so on.

Thererfore, the solution is: generate a pair `ServerPub', ServerPri'` by ourselves, and use `ServerPub'` to replace `ServerPub` in `UserAssembly.dll`, so that the client can trust the server. 

## How is UA Patch created?

We shall directly go to a imperfect conclusion:

**What encryption did was to separate RSA key value into 8-bit parts, and insert "useless" characters between each two of them.**

（It's probably not useless, but I can't use or understand it）

Let's check it out with HxD: 

![HxD Screenshot](https://raw.githubusercontent.com/YYHEggEgg/GIUserAssemblyHelper/main/HxD-Example.jpg)

It starts with `<RSAKeyV`, then a useless part `H‰Hº`. Then it's 8 valid bytes `alue><Mo`, following with another useless part `H‰PHº`, and so on...

Noticed that each part of valid value is 8 bits long. The useless parts don't have the same length, but they are all starts with `H` (0x48), and ends with `º` (0xBA).

To sum up, after dropping useless parts and connecting valid parts together, we get the RSA key value.

This is a program based on this trick, which is hosted on GitHub: [GIUserAssemblyHelper](https://github.com/YYHEggEgg/GIUserAssemblyHelper)

## Afterwards - Usage

Notice: don't directly use gotten C# XML Key, only to find stuck with following problems: 

- The program outputs 2 RSAKeyValues. Obviously, you can guess the longer one is the private key (`ClientPri`), and the shorter one is the public key (`ServerPub`).

- It outputs is neither PEM format nor other, but a common format in C# (or .NET).  
  For a public key, it's two paramters `Modulus` and `Exponent`. You may refer to the RSA encryption process.   
  
  By using software like **openssl**, you can generate PEM format keys from Modulus and Exponent.
  
  ### Generate the public key from Modulus and Exponent by openssl
  
  Reference: [StackOverflow: Creating a rsa public key from its modulus and exponent](https://stackoverflow.com/questions/11541192/creating-a-rsa-public-key-from-its-modulus-and-exponent)
  
  Firstly, create an ASN1 template file like this (you can name it as `agpub.asn1`): 
  ```asn1
  # Start with a SEQUENCE
  asn1=SEQUENCE:pubkeyinfo

  # pubkeyinfo contains an algorithm identifier and  the public key wrapped
  # in a BIT STRING
  [pubkeyinfo]
  algorithm=SEQUENCE:rsa_alg
  pubkey=BITWRAP,SEQUENCE:rsapubkey

  # algorithm ID for RSA is just an OID and a NULL
  [rsa_alg]
  algorithm=OID:rsaEncryption
  parameter=NULL

  # Actual public key: modulus and exponent
  [rsapubkey]
  n=INTEGER:0x%%MODULUS%%

  e=INTEGER:0x%%EXPONENT%%
  ```
  
  Then find the RSA key value you have gotten, which looked like this after xml-formatted: 
  ```xml
  <RSAKeyValue>
    <Modulus>lCwd...</Modulus>
    <Exponent>AQAB</Exponent>
  </RSAKeyValue>
  ```
  Base64-decode each of them and output as Hex format. And replace %%MODULUS%% in the original file with decoded Modulus value, %%EXPONENT%% with decoded Exponent value.
  
  Done below instructions, your file should be like:
  ```asn1
  # Start with a SEQUENCE
  asn1=SEQUENCE:pubkeyinfo

  # pubkeyinfo contains an algorithm identifier and  the public key wrapped
  # in a BIT STRING
  [pubkeyinfo]
  algorithm=SEQUENCE:rsa_alg
  pubkey=BITWRAP,SEQUENCE:rsapubkey

  # algorithm ID for RSA is just an OID and a NULL
  [rsa_alg]
  algorithm=OID:rsaEncryption
  parameter=NULL

  # Actual public key: modulus and exponent
  [rsapubkey]
  n=INTEGER:0x942C1D...

  e=INTEGER:0x10001
  ```
  
  Then run these commands: 
  ```shell
  openssl asn1parse -genconf acpub.asn1 -out ag_pubkey.der -noout # Generate DER Format Key
  openssl rsa -in ag_pubkey.der -inform der -pubin -out ag_pubkey.pem # Convert into PEM Format
  ```
  Generated `ag_pubkey.pem` is the public key needed.  
  
  ### Generate the private key with other paramters
  
  For the private key, its paramters are a lot more than the public key, so I leave some research details here, using openssl:
  ```shell
  PS> openssl rsa -in ...\5.pem -text -noout
  RSA Private-Key: (2048 bit, 2 primes)
  modulus:
    00:b0:96:...
  publicExponent: 65537 (0x10001)
  privateExponent:
    3c:ca:5a:...
  prime1:
    00:d7:dc:...
  prime2:
    00:d1:6d:...
  exponent1:
    00:b9:db:...
  exponent2:
    00:ad:d8:...
  coefficient:
    70:6c:43:...
  ```
  Corrsponding, the extracted RSA key value is: 
  ```xml
  <RSAKeyValue>
    <Modulus>sJbF...</Modulus>
    <Exponent>AQAB</Exponent>
    <P>19wQ...</P>
    <Q>0W09...</Q>
    <DP>udt1...</DP>
    <DQ>rdgi...</DQ>
    <InverseQ>cGxD...</InverseQ>
    <D>PMpa...</D>
  </RSAKeyValue>
  ```
  After some analyzing, we can conclude that:
  
  - Extrated Modulus <-> `modulus` output by openssl；
  - Exponent <-> publicExponent；
  - P <-> prime1
  - Q <-> prime2
  - DP <-> exponent1
  - DQ <-> exponent2
  - InverseQ <-> coefficient
  - D <-> privateExponent
  
  So, you can generate RSA private key with the following ASN1 template: 
  ```asn1
	# Start with a SEQUENCE
	asn1=SEQUENCE:rsa_privatekey

	[rsa_privatekey]
	version=INTEGER:0

	n=INTEGER:0x%%Exponent%%

	e=INTEGER:0x%%Modulus%%

	d=INTEGER:0x%%D%%

	p=INTEGER:0x%%P%%

	q=INTEGER:0x%%Q%%

	exp1=INTEGER:0x%%DP%%

	exp2=INTEGER:0x%%DQ%%

	coeff=INTEGER:0x%%InverseQ%%
  ```
  Replace each field with the Hex string you can get by base64-decoding certain fields in `RSAKeyValue`, and name the file as `acpri.asn1`。
  
  Run the following commands: 
  ```shell
  openssl asn1parse -genconf acpri.asn1 -out ag_prikey.der -noout # Generate DER Format Key
  openssl rsa -in ag_prikey.der -inform der -out ag_prikey.pem # Convert into PEM Format
  ```
  
  Got it! You have the private key now.  
  
  - Some programs may require `.der` as the key format. For information on converting PEM to other formats, readers are encouraged to search for it themselves.

Thank you for reading this far!

<details> <summary>Here comes the ad time!</summary>
Confused by various concepts in the article? Lack of automated tools?

[YYHEggEgg/csharp-Protoshift](https://github.com/YYHEggEgg/csharp-Protoshift) provides many useful features for your daily work, including `query_cur_region` encryption/decryption (with **signature verification**), Ec2b decryption, XOR key generation based on MT19937, Protobuf serialization/deserialization, and more.

It supports loading RSA keys in PEM format (PKCS1, PKCS8), as well as directly loading RSA keys in C# XML format, and supports loading private keys instead of public keys.

`csharp-Protoshift` is actually not designed as a tool program. For more dedicated and convenient tool releases, you can also join the [Discord server](https://discord.gg/NcAjuCSFvZ) to get the latest news!
</details>

## Appendix: Timeline of 'Minor' Events

- `2.8_rel`: RSA-based `query_cur_region` validity verification and KCP channel encryption were introduced for the first time. At this time, most RSA patches were completed by patching `global-metadata.dat`.
- `3.1_rel`: `global-metadata` was migrated to `MHY0` format, and the decryptor for it has not appeared as of the revision of this article. `RSAKeyValue` can be found in `UserAssembly.dll` and UA Patch can be performed.
- `3.3_rel`: `HomoKProtect.sys` went online, and modifying `UserAssembly.dll` would prevent the game from starting.
