/**
 * AutoConvertMedia.cs -- MAGIX Vegas Pro 14 Script
 *
 * Lets you pick unsupported media files (MKV, MOV, WEBM, AVI, FLV...),
 * converts them to MP4 via FFmpeg, then imports the results into Vegas.
 *
 * REQUIREMENTS:
 *   FFmpeg on your PATH (or edit FFMPEG_PATH below).
 *   Download: https://ffmpeg.org/download.html
 *
 * HOW TO RUN:
 *   Tools > Scripting > Run Script... > AutoConvertMedia.cs
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    // -- CONFIGURATION -------------------------------------------------------
    // Full path to ffmpeg.exe, or just "ffmpeg" if it is on your PATH.
    private const string FFMPEG_PATH = "ffmpeg";

    // FFmpeg quality: 18 = near-lossless, 23 = default, 28 = smaller files
    private const int VIDEO_CRF = 18;
    // -- END CONFIGURATION ---------------------------------------------------

    Vegas myVegas;

    public void FromVegas(Vegas vegas)
    {
        myVegas = vegas;

        // Check FFmpeg first before bothering the user with a file picker
        if (!IsFfmpegAvailable())
        {
            MessageBox.Show(
                "FFmpeg was not found!\n\n" +
                "Please install FFmpeg and make sure it is on your system PATH,\n" +
                "or edit the FFMPEG_PATH constant at the top of this script.\n\n" +
                "Download: https://ffmpeg.org/download.html",
                "AutoConvert -- FFmpeg Missing",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Ask user to pick files
        OpenFileDialog ofd = new OpenFileDialog();
        ofd.Title = "Select media files to convert and import";
        ofd.Multiselect = true;
        ofd.Filter =
            "Video files (*.mkv;*.mov;*.webm;*.flv;*.avi;*.ts;*.vob;*.3gp;*.ogv;*.f4v)|" +
            "*.mkv;*.mov;*.webm;*.flv;*.avi;*.ts;*.vob;*.3gp;*.ogv;*.f4v|" +
            "All files (*.*)|*.*";

        if (ofd.ShowDialog() != DialogResult.OK) return;

        string[] files = ofd.FileNames;
        if (files == null || files.Length == 0) return;

        // Show confirmation
        System.Text.StringBuilder fileListSb = new System.Text.StringBuilder();
        foreach (string f in files)
            fileListSb.AppendLine("  - " + Path.GetFileName(f));

        string confirmMsg = string.Format(
            "Convert and import {0} file(s)?\n\n{1}\n" +
            "Output: MP4 (H.264 / AAC), placed next to each original.\n" +
            "Originals will NOT be deleted.",
            files.Length, fileListSb.ToString());

        DialogResult confirm = MessageBox.Show(
            confirmMsg,
            "AutoConvert Media",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes) return;

        // Convert each file
        int ok = 0;
        int failed = 0;
        System.Text.StringBuilder resultLog = new System.Text.StringBuilder();

        foreach (string src in files)
        {
            string dst = BuildOutputPath(src);
            resultLog.AppendLine("[" + Path.GetFileName(src) + "]");

            if (File.Exists(dst))
            {
                resultLog.AppendLine("  -> Already converted, importing directly.\n");
                ImportIntoVegas(dst);
                ok++;
                continue;
            }

            string error;
            bool success = RunFfmpeg(src, dst, out error);

            if (success)
            {
                resultLog.AppendLine("  -> " + Path.GetFileName(dst) + " OK\n");
                ImportIntoVegas(dst);
                ok++;
            }
            else
            {
                resultLog.AppendLine("  FAILED: " + error + "\n");
                failed++;
            }
        }

        string summary = string.Format(
            "Done!  {0} converted and imported, {1} failed.\n\n{2}",
            ok, failed, resultLog.ToString());

        MessageBox.Show(
            summary,
            "AutoConvert -- Finished",
            MessageBoxButtons.OK,
            failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    // Import the converted file into the Vegas project media pool
    private void ImportIntoVegas(string path)
    {
        try
        {
            myVegas.Project.MediaPool.AddMedia(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not import " + Path.GetFileName(path) + "\n\n" + ex.Message,
                "AutoConvert -- Import Warning",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private bool RunFfmpeg(string src, string dst, out string error)
    {
        string args = string.Format(
            "-y -i \"{0}\" -c:v libx264 -crf {1} -preset fast " +
            "-c:a aac -b:a 192k -map 0 -movflags +faststart \"{2}\"",
            src, VIDEO_CRF, dst);

        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName               = FFMPEG_PATH;
        psi.Arguments              = args;
        psi.RedirectStandardError  = true;
        psi.RedirectStandardOutput = true;
        psi.UseShellExecute        = false;
        psi.CreateNoWindow         = true;

        try
        {
            using (Process proc = Process.Start(psi))
            {
                // Read stderr on a background thread to prevent buffer deadlock
                string stderr = "";
                System.Threading.Thread stderrThread = new System.Threading.Thread(
                    delegate()
                    {
                        stderr = proc.StandardError.ReadToEnd();
                    });
                stderrThread.Start();

                // Drain stdout too so it never blocks
                System.Threading.Thread stdoutThread = new System.Threading.Thread(
                    delegate()
                    {
                        proc.StandardOutput.ReadToEnd();
                    });
                stdoutThread.Start();

                proc.WaitForExit();
                stderrThread.Join();
                stdoutThread.Join();

                if (proc.ExitCode == 0 && File.Exists(dst))
                {
                    error = null;
                    return true;
                }

                string[] lines = stderr.Split('\n');
                int tail = Math.Max(0, lines.Length - 6);
                error = string.Join("\n", lines, tail, lines.Length - tail).Trim();
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool IsFfmpegAvailable()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName               = FFMPEG_PATH;
            psi.Arguments              = "-version";
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute        = false;
            psi.CreateNoWindow         = true;

            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
        }
        catch { return false; }
    }

    private static string BuildOutputPath(string src)
    {
        string dir  = Path.GetDirectoryName(src);
        string name = Path.GetFileNameWithoutExtension(src);
        return Path.Combine(dir, name + "_converted.mp4");
    }
}
