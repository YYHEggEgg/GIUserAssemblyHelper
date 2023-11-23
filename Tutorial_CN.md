[EN](Tutorial.md) | 中文

## 提纲

1. [引入](#引入)
2. [为什么要补丁 UA？](#为什么要补丁-ua)
3. [UA补丁到底是如何开发的？](#ua补丁到底是如何开发的)
4. [后记：实际使用](#后记实际使用)
5. [附录：小事时间线](#附录小事时间线)

## 引入

众所周知，在 3.2 版本之前，游玩某二游私服的一个必要条件就是对 `UserAssembly.dll` 进行补丁。

随着 `HomoPro.sys` 的实装，UA Patch 已成为历史，但我认为这部分内容仍有研究的价值。

正如本世纪最伟大的科学家 沃兹及·硕得 的名言，

> 世上最大的罪恶莫过于人求之以渔，却只予之以鱼。

## 为什么要补丁 UA？

要了解这部分内容，我们需要大致了解一下 Dispatch 与客户端通信的全过程。

Dispatch 主要只干两件事：`query_region_list` 和 `query_cur_region`。

在账号登录完成后（或是之前，不重要），客户端向 Dispatch 发送 `query_region_list` 请求，主要使用的参数有当前的版本号。

可以来看一个典型的 `query_region_list` 记录：

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

`query_region_list` 发回一个 base64 字符串，主要包含以下信息：

- 可选的区服列表  
  包含了区服类型，名称，`query_cur_region` 的 url 等信息。
  
- 完整的 `client_secret_key`  
  通常以 Ec2b 开头，我们也可称它使用 Ec2b 格式。它可以用于生成 `server_secret_key`（有时称之为 `dispatchKey`）。
  
- `client_custom_config`，客户端相关配置。与 `dispatchKey` 进行了异或加密。

如果区服不止一个的话，客户端应该会列出可选的区服。

在你选完以后，客户端便会向刚刚获取到的区服 dispatch url 发送 `query_cur_region` 请求。

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

响应是一个 json，包含了 `content` 和 `sign`，这两样东西都是经过 RSA 加密的。

为了讲清楚它们与加密间的关系，我们不妨设：

- `content` 加解密所用的一对密钥为 `ClientPub, ClientPri`；
- `sign` 加解密所用的一对密钥为 `ServerPub, ServerPri`。

`content` 就是 Protobuf 正文内容，是典型的公钥加密，私钥解密；而 `sign` 正好反过来，更确切的说，是私钥签名，公钥验签。

按理来说，既然都是服务端负责加密，客户端也就只有解密的份。因此，客户端具有的正是两个用于解密的密钥 `ClientPri, ServerPub`，而它们都藏在主角 `UserAssembly.dll` 里。

我们可以使用私钥来生成公钥，因此 `ClientPub` 同样已知。

最终，我们只有 `ServerPri` 未知。而开发者达到了它的目的：`sign` 字段本就为了验证服务器而生，没有私钥就没有办法生成可用的签名文件。

> 事实上，最早的 `query_cur_region` 一样只返回一个 base64 字符串，直接是 Protobuf 内容。可以去看看[当时的 dispatch 实现](https://github.com/Grasscutters/Grasscutter/blob/41365c38d7bf143f7263b90e891401ae820320d9/src/main/java/emu/grasscutter/server/dispatch/DispatchServer.java#L134)就是这样直接处理的。

> 首次观测到这样的改动是在 2.7.5（即 2.8 测试服）时，在这个版本开发者上线了大量阻止私服继续扩张的手段，除了 dispatch 还有对 KCP 流量的加密手段等等。

因此，解决方案是：自己生成一对 `ServerPub',ServerPri'` 使用，并用 `ServerPub'` 替换掉 `UserAssembly.dll` 里原有的 `ServerPub`，使得客户端信任服务器。

## UA补丁到底是如何开发的？

这里直接讲一个以偏概全但是能跑就行的结论：

**加密将 RSA 值拆成了 8 位一组，并在中间插入了“垃圾”字符。**

（当然这个垃圾指的是我自己垃圾，没有办法解析）

让我们通过 HxD 观察一下：

![HxD Screenshot](https://raw.githubusercontent.com/YYHEggEgg/GIUserAssemblyHelper/main/HxD-Example.jpg)

首先是 8 位的有效值 `<RSAKeyV`，然后是一段垃圾字符 `H‰Hº`；之后又是 8 位的有效值 `alue><Mo`，跟着一段垃圾字符 `H‰PHº`；...

注意到有效值每一组都是 8 位不变，而垃圾字符长度不定，但总以 `H`（0x48）开头，`º`（0xBA）结尾。

因此，剔除掉垃圾字符，将有效字符拼接起来，就可以得到 RSA Key 的值。

这是一个根据该原理写出的程序，托管在 Github 上：[GIUserAssemblyHelper](https://github.com/YYHEggEgg/GIUserAssemblyHelper)

## 后记：实际使用

请注意：不要跑完程序之后就直接使用得到的 C# XML key，否则可能会一脸懵逼的遇到以下问题：

- 程序输出的有两串 RSAKeyValue，显然感性猜测一定能知道长的是私钥（`ClientPri`），短的是公钥（`ServerPub`）。

- 它输出的既不是 PEM 格式也不是其他的格式，而是 C#（或 .NET）里面一种常用的格式。  
  对于公钥，它使用的是两个参数 `Modulus`（模数）和 `Exponent`（指数）。具体可以参考 RSA 加密的原理。  
  您可以通过 openssl 等软件通过模数和指数生成 PEM 格式的密钥。
  
  ### 通过 openssl 以模数与指数生成公钥
  
  参考 [StackOverflow: Creating a rsa public key from its modulus and exponent](https://stackoverflow.com/questions/11541192/creating-a-rsa-public-key-from-its-modulus-and-exponent)
  
  首先创建这样一个 ASN1 模板文件（例中命名为 `agpub.asn1`）：
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
  
  然后把你获取到的 RSA 密钥拿出来，它格式化后看起来像这样：
  ```xml
  <RSAKeyValue>
    <Modulus>lCwd...</Modulus>
    <Exponent>AQAB</Exponent>
  </RSAKeyValue>
  ```
  把他们以 base64 解密后，以十六进制输出，并将 Modulus 填入原文件中的 %%MODULUS%%，将 Exponent 填入原文件中的 %%EXPONENT%%。
  
  改完以后您的文件看起来像这样：
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
  
  然后执行以下命令：
  ```shell
  openssl asn1parse -genconf acpub.asn1 -out ag_pubkey.der -noout # Generate DER Format Key
  openssl rsa -in ag_pubkey.der -inform der -pubin -out ag_pubkey.pem # Convert into PEM Format
  ```
  生成的 `ag_pubkey.pem` 就是所需的公钥。
  
  ### 以其他参数生成私钥
  
  对于私钥，其参数更多更杂，因此在这里附上使用了 openssl 的研究细节：
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
  对应的，提取的 RSA 密钥为：
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
  进行一些分析，得到以下结论：
  
  - 提取的 Modulus 对应 openssl 输出的 modulus；
  - Exponent <-> publicExponent；
  - P <-> prime1
  - Q <-> prime2
  - DP <-> exponent1
  - DQ <-> exponent2
  - InverseQ <-> coefficient
  - D <-> privateExponent
  
  因此，您可以通过以下 ASN1 模板生成 RSA 私钥：
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
  将字段替换为 `RSAKeyValue` 中 base64 解密得到的 Hex 字符串，并将文件命名为 `acpri.asn1`。
  
  执行以下命令：
  ```shell
  openssl asn1parse -genconf acpri.asn1 -out ag_prikey.der -noout # Generate DER Format Key
  openssl rsa -in ag_prikey.der -inform der -out ag_prikey.pem # Convert into PEM Format
  ```
  
  您就得到了 PEM 格式的私钥。

- 某些程序可能需求 `.der` 作为密钥格式。有关 PEM 转换为其他格式的资料还请读者自行查询。

感谢您耐心读到这里！
  
<details> <summary>下面是广告时间！</summary>
对文中的各种概念一头雾水？缺少自动化的工具？

[YYHEggEgg/csharp-Protoshift](https://github.com/YYHEggEgg/csharp-Protoshift) 可以支持包括 `query_cur_region` 加解密（以及**验证签名**）、Ec2b 解密、基于 MT19937 的 XOR Key 生成、Protobuf 序列化/反序列化等诸多有助于您日常工作的功能。

它支持加载 PKCS1、PKCS8 的 PEM 格式 RSA 密钥，以及**直接加载 C# XML 格式的 RSA 密钥**，并且支持以私钥代替公钥加载。

`csharp-Protoshift` 实际上不是专被设计成此类工具的程序。有关更为专用与便捷的工具发布，也可以加入 [Discord 服务器](https://discord.gg/NcAjuCSFvZ) 来获取最新消息！
</details>

## 附录：小事时间线

- `2.8_rel`：首次加入了基于 RSA 的 `query_cur_region` 有效性验证和 KCP 信道加密。此时 RSA Patch 大多由 Patch `global-metadata.dat` 完成。
- `3.1_rel`：`global-metadata` 迁移至了 `MHY0` 格式，其 decryptor 在本文修订时仍未出现。`UserAssembly.dll` 中可以找到 `RSAKeyValue` 并执行 UA Patch。
- `3.3_rel`：`HomoKProtect.sys` 上线，更改文件 `UserAssembly.dll` 将无法启动游戏。
