#region License
/* Copyright 2012 James F. Bellinger <http://www.zer7.com/software/hidsharp>
 * Copyright 2017 David Lechner <david@lechnology.com>

   Permission to use, copy, modify, and/or distribute this software for any
   purpose with or without fee is hereby granted, provided that the above
   copyright notice and this permission notice appear in all copies.

   THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
   WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
   MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
   ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
   WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
   ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
   OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE. */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;

using static HidSharp.Platform.Linux.NativeMethods;

namespace HidSharp.Platform.Linux
{
    class LinuxHidManager : HidManager
    {
        IntPtr udev;
        IntPtr monitor;
        IList<string> paths = new SynchronizedCollection<string>();

        public override void Init()
        {
            // TODO: should probably do some better error checking here
            udev = udev_new();
            monitor = udev_monitor_new_from_netlink(udev, "udev");
            udev_monitor_filter_add_match_subsystem_devtype(monitor, "hidraw", null);
            udev_monitor_enable_receiving(monitor);
            var enumerate = udev_enumerate_new(udev);
            try {
                udev_enumerate_add_match_subsystem(enumerate, "hidraw");
                udev_enumerate_scan_devices(enumerate);
                for (var entry = udev_enumerate_get_list_entry(enumerate);
                     entry != IntPtr.Zero;
                     entry = udev_list_entry_get_next(entry))
                {
                    var syspath = udev_list_entry_get_name(entry);
                    if (syspath != null) {
                        paths.Add(syspath);
                    }
                }
            }
            finally {
                udev_enumerate_unref(enumerate);
            }
            // FIXME: Need to implement IDisposeable and free udev and monitor
        }

        public override void Run()
        {
            var fds = new pollfd[1];
            fds[0].fd = udev_monitor_get_fd(monitor);
            fds[0].events = pollev.IN;
            while (true) {
                int ret = retry(() => poll(fds, (IntPtr)fds.Length, -1));
                if (ret == -1) {
                    // FIXME: how do we notify the main program that something bad happened here?
                    break;
                }
                var device = udev_monitor_receive_device(monitor);
                try {
                    var action = udev_device_get_action(device);
                    var syspath = udev_device_get_syspath(device);
                    switch (action) {
                    case "add":
                        if (syspath != null) {
                            paths.Add(syspath);
                        }
                        break;
                    case "remove":
                        paths.Remove(syspath);
                        break;
                    }
                } finally {
                    udev_device_unref(device);
                }
            }
        }

        protected override object[] Refresh()
        {
            return paths.Cast<object>().ToArray();
        }

        protected override bool TryCreateDevice(object key, out HidDevice device, out object creationState)
        {
            creationState = null;
            string syspath = (string)key; var hidDevice = new LinuxHidDevice(syspath);
            if (!hidDevice.GetInfo()) { device = null; return false; }
            device = hidDevice; return true;
        }

        protected override void CompleteDevice(object key, HidDevice device, object creationState)
        {
            
        }

        public override bool IsSupported {
            get {
                // basically, we are just testing if libudev is present
                try {
                    var udev = NativeMethods.udev_new();
                    if (udev == IntPtr.Zero) {
                        return false;
                    }
                    NativeMethods.udev_unref(udev);
                    return true;
                }
                catch (DllNotFoundException) {
                    return false;
                }
            }
        }
    }
}
