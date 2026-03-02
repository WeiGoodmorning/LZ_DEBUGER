using FluentFTP;
using System;
using System.IO;
using System.Threading.Tasks;

namespace LZ
{


    public class FluentFtpClient
    {
        private AsyncFtpClient _client;

        public FluentFtpClient(string host, string username, string password)
        {
            _client = new AsyncFtpClient(host, username, password);
        }

        // 连接FTP服务器
        public async Task ConnectAsync()
        {
            await _client.Connect();
            Console.WriteLine("Connected to FTP server");
        }

        // 断开连接
        public async Task DisconnectAsync()
        {
            await _client.Disconnect();
            Console.WriteLine("Disconnected from FTP server");
        }

        // 上传文件
        public async Task UploadFileAsync(string localFilePath, string remotePath)
        {
            try
            {
                var status = await _client.UploadFile(localFilePath, remotePath);
                Console.WriteLine($"Upload status: {status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading file: {ex.Message}");
            }
        }

        // 下载文件
        public async Task DownloadFileAsync(string remotePath, string localFilePath)
        {
            try
            {
                var status = await _client.DownloadFile(localFilePath, remotePath);
                Console.WriteLine($"Download status: {status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading file: {ex.Message}");
            }
        }

        // 列出文件
        public async Task ListFilesAsync(string remotePath = "/")
        {
            try
            {
                var items = await _client.GetListing(remotePath);
                foreach (var item in items)
                {
                    Console.WriteLine($"{item.Type} - {item.Name} - {item.Size} bytes");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing files: {ex.Message}");
            }
        }

        // 创建目录
        public async Task CreateDirectoryAsync(string directoryPath)
        {
            try
            {
                await _client.CreateDirectory(directoryPath, true);
                Console.WriteLine($"Directory created: {directoryPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating directory: {ex.Message}");
            }
        }

        // 删除文件
        public async Task DeleteFileAsync(string remotePath)
        {
            try
            {
                await _client.DeleteFile(remotePath);
                Console.WriteLine($"File deleted: {remotePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting file: {ex.Message}");
            }
        }
    }
}