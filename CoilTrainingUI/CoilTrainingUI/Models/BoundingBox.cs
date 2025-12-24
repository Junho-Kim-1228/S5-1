using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoilTrainingUI.Models
{
    public class BoundingBox
    {
        public double X { get; set; }      // normalized (0~1)
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public string ClassName { get; set; } // dent / loose
    }
}