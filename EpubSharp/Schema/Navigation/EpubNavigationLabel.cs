﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EpubSharp.Schema.Navigation
{
    public class EpubNavigationLabel
    {
        public string Text { get; set; }

        public override string ToString()
        {
            return Text;
        }
    }
}
