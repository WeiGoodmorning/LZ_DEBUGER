using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;
using Renci.SshNet;

namespace LZ
{
    public class FtpClient
    {
        private string server;
        private int port;
        private string username;
        private string password;

        public async Task<bool> ConnectAsync(string server, int port, string username, string password)
        {
            this.server = server;
            this.port = port;
            this.username = username;
            this.password = password;

            try
            {
                // 测试连接
                var request = CreateFtpRequest("/", WebRequestMethods.Ftp.ListDirectory);
                using (var response = await request.GetResponseAsync())
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<FileInfo>> ListFilesAsync(string path)
        {
            var files = new List<FileInfo>();

            try
            {
                var request = CreateFtpRequest(path, WebRequestMethods.Ftp.ListDirectoryDetails);

                using (var response = await request.GetResponseAsync())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        var fileInfo = ParseFileInfo(line);
                        if (fileInfo != null)
                        {
                            files.Add(fileInfo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"获取文件列表失败: {ex.Message}");
            }

            return files;
        }

        public async Task DownloadFileAsync(string remotePath, string localPath)
        {
            try
            {
                var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.DownloadFile);

                using (var response = await request.GetResponseAsync())
                using (var stream = response.GetResponseStream())
                using (var fileStream = File.Create(localPath))
                {
                    await stream.CopyToAsync(fileStream);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"下载文件失败: {ex.Message}");
            }
        }

        public async Task UploadFileAsync(string localPath, string remotePath)
        {
            try
            {
                var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.UploadFile);

                using (var fileStream = File.OpenRead(localPath))
                using (var requestStream = await request.GetRequestStreamAsync())
                {
                    await fileStream.CopyToAsync(requestStream);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"上传文件失败: {ex.Message}");
            }
        }

        public async Task DeleteFileAsync(string remotePath)
        {
            try
            {
                var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.DeleteFile);
                using (var response = await request.GetResponseAsync()) { }
            }
            catch (Exception ex)
            {
                throw new Exception($"删除文件失败: {ex.Message}");
            }
        }

        public async Task DeleteDirectoryAsync(string remotePath)
        {
            try
            {
                var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.RemoveDirectory);
                using (var response = await request.GetResponseAsync()) { }
            }
            catch (Exception ex)
            {
                throw new Exception($"删除目录失败: {ex.Message}");
            }
        }

        //public async Task CreateDirectoryAsync(string remotePath)
        //{
        //    try
        //    {
        //        var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.MakeDirectory);
        //        using (var response = await request.GetResponseAsync()) { }
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new Exception($"创建目录失败: {ex.Message}");
        //    }
        //}

        public async Task CreateDirectoryAsync(string remotePath)
        {
            try
            {
                var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.MakeDirectory);
                using (var response = await request.GetResponseAsync()) { }
            }
            catch (WebException ex)
            {
                var response = ex.Response as FtpWebResponse;
                // FTP协议中，550状态码对于MKD(创建目录)命令通常意味着“目录已存在”或“无权限”
                // 在这里我们宽容处理，如果抛出550，直接当做目录已经存在
                if (response != null && response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                {
                    return;
                }
                throw new Exception($"创建目录失败: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"创建目录失败: {ex.Message}");
            }
        }

        public async Task RenameAsync(string oldPath, string newPath)
        {
            try
            {
                var request = CreateFtpRequest(oldPath, WebRequestMethods.Ftp.Rename);
                request.RenameTo = newPath.Split('/')[^1];
                using (var response = await request.GetResponseAsync()) { }
            }
            catch (Exception ex)
            {
                throw new Exception($"重命名失败: {ex.Message}");
            }
        }

        public async Task<bool> FileExistsAsync(string remotePath)
        {
            try
            {
                // 尝试获取文件/目录的大小信息，如果返回成功则存在
                var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.GetFileSize);
                using (var response = await request.GetResponseAsync())
                {
                    return true;
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as FtpWebResponse;
                if (response != null)
                {
                    // 550 表示文件未找到或不可访问
                    if (response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                    {
                        return false;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }


        // 新增：专门用于判断目录是否存在
        public async Task<bool> DirectoryExistsAsync(string remotePath)
        {
            try
            {
                // 使用 ListDirectory 命令，如果目录存在且有权限，则会返回成功
                var request = CreateFtpRequest(remotePath, WebRequestMethods.Ftp.ListDirectory);
                using (var response = await request.GetResponseAsync())
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // 设置文件权限（使用SITE CHMOD命令）
        //public async Task<bool> SetFilePermissionsAsync(string remotePath, string permissions)
        //{
        //    try
        //    {


        //        var request = CreateFtpRequest(remotePath, "SITE CHMOD");
        //        //request.Method = WebRequestMethods.Ftp.SendCommand;

        //        // 构建SITE CHMOD命令
        //        string command = $"CHMOD {permissions} {remotePath.Split('/').Last()}";
        //        byte[] commandBytes = Encoding.UTF8.GetBytes(command);

        //        using (var requestStream = await request.GetRequestStreamAsync())
        //        {
        //            await requestStream.WriteAsync(commandBytes, 0, commandBytes.Length);
        //        }

        //        using (var response = await request.GetResponseAsync())
        //        {
        //            return true;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"设置文件权限失败: {ex.Message}");
        //        return false;
        //    }
        //}
        // 设置文件权限（改用更稳定的 SSH 方式）
        public async Task<bool> SetFilePermissionsAsync(string remotePath, string permissions)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // FTP默认端口是21，SSH默认是22。这里使用已保存的 server, username, password 进行连接
                    using (var sshClient = new SshClient(server, 22, username, password))
                    {
                        sshClient.Connect();

                        // 构建并执行 chmod 命令，注意这里直接对完整路径赋权
                        string command = $"chmod {permissions} {remotePath}";
                        var cmd = sshClient.RunCommand(command);

                        sshClient.Disconnect();

                        // ExitStatus 为 0 表示命令执行成功
                        if (cmd.ExitStatus != 0)
                        {
                            Console.WriteLine($"SSH命令返回错误: {cmd.Error}");
                        }

                        return cmd.ExitStatus == 0;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"设置文件权限失败: {ex.Message}");
                    return false;
                }
            });
        }

        // 新增：在 FtpClient 类中添加 MoveFileAsync 方法；并调整 RenameAsync 使其使用传入的 newPath（允许完整路径）
        public async Task MoveFileAsync(string sourcePath, string destPath)
        {
            // 先尝试服务器端重命名/移动（RNFR/RNTO）
            try
            {
                var request = CreateFtpRequest(sourcePath, WebRequestMethods.Ftp.Rename);
                // 尝试将 RenameTo 设置为目标路径（有些 FTP 服务端支持带路径的 RNTO）
                request.RenameTo = destPath;
                using (var response = await request.GetResponseAsync())
                {
                    return;
                }
            }
            catch (WebException ex)
            {
                // 如果服务器不支持直接移动（或失败），回退到下载->上传->删除原文件
                Console.WriteLine($"设置文件权限失败: {ex.Message}");
            }

            
        }

        // 检查连接状态
        public bool IsConnected()
        {
            return !string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(username);
        }

        private FtpWebRequest CreateFtpRequest(string path, string method)
        {
            var uri = new Uri($"ftp://{server}:{port}{path}");
            var request = (FtpWebRequest)WebRequest.Create(uri);
            request.Method = method;
            request.Credentials = new NetworkCredential(username, password);
            request.UsePassive = true;
            request.UseBinary = true;
            request.KeepAlive = false;

            return request;
        }

        private FileInfo ParseFileInfo(string line)
        {
            // Linux FTP LIST 格式解析
            // 示例: "drwxr-xr-x 2 user group 4096 Dec 10 14:30 directory"
            //        "-rw-r--r-- 1 user group 1024 Dec 10 14:30 file.txt"

            if (string.IsNullOrWhiteSpace(line) || line.Length < 10)
                return null;

            try
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 9) return null;

                var permissions = parts[0];
                var isDirectory = permissions[0] == 'd';
                var name = parts[8];

                // 处理包含空格的文件名
                for (int i = 9; i < parts.Length; i++)
                {
                    name += " " + parts[i];
                }

                var fileInfo = new FileInfo
                {
                    Name = name,
                    Type = isDirectory ? "目录" : "文件",
                    Permissions = permissions,
                    ModifiedDate = $"{parts[5]} {parts[6]} {parts[7]}"
                };

                if (!isDirectory && parts[4] != null)
                {
                    if (long.TryParse(parts[4], out long size))
                    {
                        fileInfo.Size = FormatFileSize(size);
                    }
                    else
                    {
                        fileInfo.Size = parts[4];
                    }
                }
                else
                {
                    fileInfo.Size = "-";
                }

                return fileInfo;
            }
            catch
            {
                return null;
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
