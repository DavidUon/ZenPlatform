using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Utility
{
    public class PersonalData
    {
        public string ProgramId { get; set; } = "PndDataEditor";
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = "試用者";
        public string BranchNo { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public DateTime ExpireDate { get; set; } = DateTime.MaxValue;
        public int MaxPosition { get; set; } = int.MaxValue;
        public string Remark { get; set; } = string.Empty;
        public bool IsNoExpireLimit { get; set; } = false;
        public bool IsNoTradeLimit { get; set; } = false;
        public bool IsTestUser { get; set; } = false;
        public bool IsUserInfoLoaded { get; set; } = false;

        // 原始字串
        private const string RawKey = "ThisIsPND2KeyVer100ForTrade!!";
        private const string RawIV = "Pnd2InitVector!!!";

        // 調整後的Key和IV
        private static readonly byte[] Key = FixKeyLength(RawKey, 32);
        private static readonly byte[] IV = FixKeyLength(RawIV, 16);

        private static byte[] FixKeyLength(string input, int length)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            if (bytes.Length > length)
                return bytes.Take(length).ToArray();
            else if (bytes.Length < length)
                return bytes.Concat(new byte[length - bytes.Length]).ToArray();
            else
                return bytes;
        }

        public void Save(string filename)
        {
            Id = (Id ?? string.Empty).ToUpperInvariant();

            string json = JsonSerializer.Serialize(this);
            byte[] encryptedData = EncryptStringToBytes_Aes(json, Key, IV);
            File.WriteAllBytes(filename, encryptedData);
        }

        public bool Load(string filename)
        {
            IsUserInfoLoaded = false;
            try
            {
                if (!File.Exists(filename))
                    return false;

                byte[] encryptedData = File.ReadAllBytes(filename);
                string json = DecryptStringFromBytes_Aes(encryptedData, Key, IV);

                var loaded = JsonSerializer.Deserialize<PersonalData>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (loaded == null)
                    return false;

                // 基本欄位
                ProgramId = loaded.ProgramId ?? ProgramId;
                Id = loaded.Id ?? string.Empty;
                Name = loaded.Name ?? Name;
                BranchNo = loaded.BranchNo ?? string.Empty;
                Password = loaded.Password ?? string.Empty;
                Account = loaded.Account ?? string.Empty;
                Remark = loaded.Remark ?? string.Empty;
                IsTestUser = loaded.IsTestUser;

                // 旗標
                IsNoExpireLimit = loaded.IsNoExpireLimit;
                IsNoTradeLimit = loaded.IsNoTradeLimit;

                // 依旗標做一致化（兼容舊檔）
                ExpireDate = (loaded.IsNoExpireLimit || loaded.ExpireDate == DateTime.MaxValue)
                                ? DateTime.MaxValue
                                : loaded.ExpireDate;

                MaxPosition = (loaded.IsNoTradeLimit || loaded.MaxPosition == int.MaxValue)
                                ? int.MaxValue
                                : loaded.MaxPosition;

                IsUserInfoLoaded = true;
                return true;
            }
            catch
            {
                return false;
            }
        }


        private static byte[] EncryptStringToBytes_Aes(string plainText, byte[] key, byte[] iv)
        {
            using Aes aesAlg = Aes.Create();
            aesAlg.Key = key;
            aesAlg.IV = iv;

            ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            using MemoryStream msEncrypt = new();
            using CryptoStream csEncrypt = new(msEncrypt, encryptor, CryptoStreamMode.Write);
            using (StreamWriter swEncrypt = new(csEncrypt))
            {
                swEncrypt.Write(plainText);
            }
            return msEncrypt.ToArray();
        }

        private static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] key, byte[] iv)
        {
            using Aes aesAlg = Aes.Create();
            aesAlg.Key = key;
            aesAlg.IV = iv;

            ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

            using MemoryStream msDecrypt = new(cipherText);
            using CryptoStream csDecrypt = new(msDecrypt, decryptor, CryptoStreamMode.Read);
            using StreamReader srDecrypt = new(csDecrypt);
            return srDecrypt.ReadToEnd();
        }
    }
}