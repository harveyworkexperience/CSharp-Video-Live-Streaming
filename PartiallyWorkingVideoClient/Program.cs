﻿using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing;

class Client
{
    // Connection Variables
    private bool connection_success = false;
    private static UdpClient udpclient = new UdpClient();
    private static IPEndPoint ep = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 11000); // endpoint where server is listening
    public static byte[] received_bytes;
    private static int num_packets = 0;
    private static int received_bytes_ptr = 0;
    private static bool is_running = false;
    private static Thread _thread;

    // Frame Variables
    private const int image_size = 5000000; // Number of bytes for an image
    private static byte[] image = new byte[image_size];
    private static byte[] ready_image;
    private static int img_number = 0;
    private static int display_num = 0;

    // Display Variables
    private static System.Windows.Forms.Timer timer;
    private static PictureBox pb;

    // State Variables
    private static bool is_ready = false;

    // Synchronisation Variables
    private static Mutex SaveMtx = new Mutex();
    private static Mutex DisplayMtx = new Mutex();

    // Function for connecting to a UDP stream server
    bool ConnectToStreamService()
    {
        // Connecting to service
        try
        {
            udpclient.Connect(ep);
            connection_success = true;
        }
        catch (Exception ex)
        {
            Console.Write(ex);
        }

        // Connection success
        if (connection_success)
        {
            // Waiting for response from server
            byte[] datagram = Encoding.ASCII.GetBytes("Are we connected yet?");
            udpclient.Send(datagram, datagram.Length);
            var data = Encoding.ASCII.GetString(udpclient.Receive(ref ep));
            while (data != "Yes we are connected.")
            {
                udpclient.Send(datagram, datagram.Length);
                data = Encoding.ASCII.GetString(udpclient.Receive(ref ep));
            }
            // Requesting byte stream
            datagram = Encoding.ASCII.GetBytes("Received response!");
            udpclient.Send(datagram, datagram.Length);
            data = Encoding.ASCII.GetString(udpclient.Receive(ref ep));
            while (data.Length <= 0)
            {
                udpclient.Send(datagram, datagram.Length);
                data = Encoding.ASCII.GetString(udpclient.Receive(ref ep));
            }
        }
        return connection_success;
    }

    // Function that returns the byte of the stream
    static byte GetStreamByte()
    {
        // Fetching bytes to store in received_bytes array
        if (received_bytes == null || received_bytes_ptr >= received_bytes.Length)
        {
            received_bytes = udpclient.Receive(ref ep);
            received_bytes_ptr = 0;
            num_packets++;
        }
        return received_bytes[received_bytes_ptr++];
    }

    // Function for populating byte array with JPEG bytes
    // Needs to be run on another thread, in the background.
    static void Build_Images_JPEG()
    {
        Console.WriteLine("T: Building image from JPEG bytes...");
        while (is_running)
        {
            // Looking for JPEG headers
            int found_jpg_header = 0;
            byte b;
            Console.WriteLine("T: Looking for JPEG headers...");
            while (true)
            {
                b = GetStreamByte();
                if (b == 0xff)
                    found_jpg_header = 1;
                else if (b == 0xd8 && found_jpg_header == 1)
                    break;
                else
                    found_jpg_header = 0;
            }

            // Start working on making a new image
            Console.WriteLine("T: Found JPEG headers!");
            // Initialising image
            int byte_count = 0;
            image = new byte[image_size];
            image[byte_count++] = 0xff;
            image[byte_count++] = 0xd8;

            // Building image
            int end_flag = 0;
            Console.WriteLine("T: Retrieving image bytes...");
            while (byte_count < image_size)
            {
                byte tmp_b = GetStreamByte();
                image[byte_count++] = tmp_b;
                if (tmp_b == 0xff && end_flag == 0)
                    end_flag++;
                else if (tmp_b == 0xd9 && end_flag == 1)
                    break;
                else
                    end_flag = 0;
            }
            // Completing image
            if (byte_count == image_size - 1)
            {
                image[image_size - 2] = 0xff;
                image[image_size - 1] = 0xd9;
            }
            // Storing completed image into another byte array
            else Array.Resize<byte>(ref image, byte_count);

            // Saving image
            Console.WriteLine("T: Retrieved image bytes!");
            ready_image = new byte[byte_count];
            Array.Copy(image, ready_image, byte_count);
            Console.WriteLine("T: Finished building image and now saving it...");
            Console.WriteLine("T: " + image.Length);
            SaveMtx.WaitOne();
            ByteArrayToFile(@"C:\Work Experience\JPEG_Images\temp_images\img" + img_number + "_tmp.jpg", ready_image);
            is_ready = true;
            img_number = (img_number + 1) % 2;
            SaveMtx.ReleaseMutex();
            Console.WriteLine("T: Image saved!");
            // Resetting
            continue;
        }
    }

    static public bool ByteArrayToFile(string fileName, byte[] byteArray)
    {
        try
        {
            using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
            {
                fs.Write(byteArray, 0, byteArray.Length);
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception caught in process: {0}", ex);
            return false;
        }
    }

    private static void Display_Image(string imagepath)
    {
        using (Form form = new Form())
        {
            Image img = Image.FromFile(imagepath);
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Size = img.Size;
            pb = new PictureBox();
            pb.Dock = DockStyle.Fill;
            pb.Image = img;
            form.Controls.Add(pb);
            form.ShowDialog();
        }
    }

    static void Main(string[] args)
    {
        Console.WriteLine("CLIENT\n=====================");

        Client c = new Client();
        _thread = new Thread(Build_Images_JPEG);

        Console.WriteLine("Connecting to server...");
        if (c.ConnectToStreamService())
        {
            Console.WriteLine("Connected to server!");
            is_running = true;
            _thread.Start();
        }
        else
        {
            Console.WriteLine("Unable to connect to server!");
            Console.WriteLine("Exiting.");
            return;
        }

        // Setting timer
        Console.WriteLine("Starting timer...");
        timer = new System.Windows.Forms.Timer();
        timer.Interval = 100; // specify interval time as you want
        timer.Tick += (sender, tick_args) =>
        {
            Console.Write(".");
            if (is_ready)
            {
                Console.Write("\n");
                Console.WriteLine("Switching image!");
                SaveMtx.WaitOne();
                if (img_number == 0)
                    pb.Image = Image.FromFile(@"C:\Work Experience\JPEG_Images\temp_images\img1_tmp.jpg");
                else
                    pb.Image = Image.FromFile(@"C:\Work Experience\JPEG_Images\temp_images\img0_tmp.jpg");
                is_ready = false;
                SaveMtx.ReleaseMutex();
                pb.Refresh();
            }
        };
        timer.Start();
        Console.WriteLine("Timer has started!");

        // Displaying image
        while (!is_ready) ;
        Display_Image(@"C:\Work Experience\JPEG_Images\temp_images\img0_tmp.jpg");

        System.Threading.Thread.Sleep(100000);
        is_running = false;
        _thread.Join();
    }
}

// Currently trying to work on loading images on the background and swapping between them