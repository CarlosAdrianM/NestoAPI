using System;

namespace NestoAPI.Models.Comisiones
{
    public class ResumenComisionesMes
    {
        public string Vendedor { get; set; }
        public int Anno { get; set; }
        public int Mes { get; set; }
        public decimal GeneralVenta { get; set; }
        public decimal GeneralProyeccion { get; set; }
        public decimal GeneralTipo { get; set; }
        public decimal GeneralComision { get; set; }
        public decimal GeneralFaltaParaSalto { get; set; }
        public decimal UnionLaserVenta { get; set; }
        public decimal UnionLaserTipo { get; set; }
        public decimal UnionLaserComision {
            get
            {
                return Math.Round(UnionLaserVenta * UnionLaserTipo, 2);
            }
        }
        public decimal EvaVisnuVenta { get; set; }
        public decimal EvaVisnuTipo { get; set; }
        public decimal EvaVisnuComision {
            get
            {
                return Math.Round(EvaVisnuVenta * EvaVisnuTipo, 2);
            }
        }
        public decimal OtrosAparatosVenta { get; set; }
        public decimal OtrosAparatosTipo { get; set; }
        public decimal OtrosAparatosComision {
            get
            {
                return Math.Round(OtrosAparatosVenta * OtrosAparatosTipo, 2);
            }
        }
        public decimal TotalComisiones {
            get
            {
                return Math.Round(GeneralComision + UnionLaserComision + EvaVisnuComision + OtrosAparatosComision, 2);
            }
        }
    }
}