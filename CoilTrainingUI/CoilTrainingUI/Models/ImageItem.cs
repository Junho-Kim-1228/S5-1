using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoilTrainingUI.Models
{
    public class ImageItem
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }

        public bool HasLabel { get; set; }
    }
}
