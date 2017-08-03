////////////////////////////////////////////////////////////////////////////////
//
// This file is part of pdn-kuwahara, a Kuwahara noise reduction Effect
// plugin for Paint.NET.
//
// Copyright (c) 2017 Nicholas Hayes
//
// This file is licensed under the MIT License.
// See LICENSE.txt for complete licensing and attribution information.
//
////////////////////////////////////////////////////////////////////////////////

using PaintDotNet;
using System;
using System.Reflection;

namespace Kuwahara
{
    public sealed class PluginSupportInfo : IPluginSupportInfo
    {
        public string DisplayName
        {
            get
            {
                return KuwaharaEffect.StaticName;
            }
        }

        public string Author
        {
            get
            {
                return ((AssemblyCompanyAttribute)(typeof(KuwaharaEffect).Assembly.GetCustomAttributes(typeof(AssemblyCompanyAttribute), false)[0])).Company;
            }
        }

        public string Copyright
        {
            get
            {
                return ((AssemblyCopyrightAttribute)(typeof(KuwaharaEffect).Assembly.GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false)[0])).Copyright;
            }
        }

        public Version Version
        {
            get
            {
                return typeof(KuwaharaEffect).Assembly.GetName().Version;
            }
        }

        public Uri WebsiteUri
        {
            get
            {
                return new Uri("http://www.getpaint.net/redirect/plugins.html");
            }
        }
    }
}
