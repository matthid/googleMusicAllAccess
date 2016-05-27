namespace GMusicAPI

// Taken & Converted From http://stackoverflow.com/questions/10168240/encrypting-decrypting-a-string-in-c-sharp 
module StringCypher =
  open System
  open System.Text
  open System.Security.Cryptography
  open System.IO
  open System.Linq
  // This constant is used to determine the keysize of the encryption algorithm in bits.
  // We divide this by 8 within the code below to get the equivalent number of bytes.
  let private keysize = 256

  // This constant determines the number of iterations for the password bytes generation function.
  let private derivationIterations = 1000
    
  let private generate256BitsOfRandomEntropy () =
    let randomBytes = Array.zeroCreate 32 // 32 Bytes will give us 256 bits.
    use rngCsp = new RNGCryptoServiceProvider()
    // Fill the array with cryptographically secure random bytes.
    rngCsp.GetBytes(randomBytes)
    randomBytes

  let encrypt (passPhrase:string) (plainText:string) =
    // Salt and IV is randomly generated each time, but is preprended to encrypted cipher text
    // so that the same Salt and IV values can be used when decrypting.  
    let saltStringBytes = generate256BitsOfRandomEntropy()
    let ivStringBytes = generate256BitsOfRandomEntropy()
    let plainTextBytes = Encoding.UTF8.GetBytes(plainText)
    use password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, derivationIterations)
    let keyBytes = password.GetBytes(keysize / 8)
    use symmetricKey = new RijndaelManaged()
    symmetricKey.BlockSize <- 256
    symmetricKey.Mode <- CipherMode.CBC
    symmetricKey.Padding <- PaddingMode.PKCS7
    use encryptor = symmetricKey.CreateEncryptor(keyBytes, ivStringBytes)
    use memoryStream = new MemoryStream()
    use cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write)
    cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length)
    cryptoStream.FlushFinalBlock()
    // Create the final bytes as a concatenation of the random salt bytes, the random iv bytes and the cipher bytes.
    let cipherTextBytes = saltStringBytes.Concat(ivStringBytes).ToArray()
    let cipherTextBytes = cipherTextBytes.Concat(memoryStream.ToArray()).ToArray()
    memoryStream.Close()
    cryptoStream.Close()
    Convert.ToBase64String(cipherTextBytes)

  let decrypt (passPhrase:string) (cipherText:string) =
    // Get the complete stream of bytes that represent:
    // [32 bytes of Salt] + [32 bytes of IV] + [n bytes of CipherText]
    let cipherTextBytesWithSaltAndIv = Convert.FromBase64String(cipherText)
    // Get the saltbytes by extracting the first 32 bytes from the supplied cipherText bytes.
    let saltStringBytes = cipherTextBytesWithSaltAndIv.Take(keysize / 8).ToArray()
    // Get the IV bytes by extracting the next 32 bytes from the supplied cipherText bytes.
    let ivStringBytes = cipherTextBytesWithSaltAndIv.Skip(keysize / 8).Take(keysize / 8).ToArray()
    // Get the actual cipher text bytes by removing the first 64 bytes from the cipherText string.
    let cipherTextBytes = cipherTextBytesWithSaltAndIv.Skip((keysize / 8) * 2).Take(cipherTextBytesWithSaltAndIv.Length - ((keysize / 8) * 2)).ToArray()

    use password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, derivationIterations)
    let keyBytes = password.GetBytes(keysize / 8)
    use symmetricKey = new RijndaelManaged()
    symmetricKey.BlockSize <- 256;
    symmetricKey.Mode <- CipherMode.CBC;
    symmetricKey.Padding <- PaddingMode.PKCS7;
    use decryptor = symmetricKey.CreateDecryptor(keyBytes, ivStringBytes)
    use memoryStream = new MemoryStream(cipherTextBytes)
    use cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read)
    let plainTextBytes = Array.zeroCreate cipherTextBytes.Length
    let decryptedByteCount = cryptoStream.Read(plainTextBytes, 0, plainTextBytes.Length)
    memoryStream.Close()
    cryptoStream.Close()
    Encoding.UTF8.GetString(plainTextBytes, 0, decryptedByteCount)
