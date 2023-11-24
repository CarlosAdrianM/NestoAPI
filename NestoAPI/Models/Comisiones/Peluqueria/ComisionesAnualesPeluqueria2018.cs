using NestoAPI.Models.Comisiones.Estetica;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class ComisionesAnualesPeluqueria2018 : IComisionesAnuales
    {
        const string GENERAL = "General";

        private NVEntities db = new NVEntities();

        public ComisionesAnualesPeluqueria2018()
        {
            Etiquetas = NuevasEtiquetas;
        }

        public ICollection<IEtiquetaComision> Etiquetas { get; set; }

        public ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
            {
                new EtiquetaGeneral(new ServicioComisionesAnualesComun()),
                new EtiquetaLisap(new ServicioComisionesAnualesComun())
            };

        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2018();

        public string EtiquetaLinea(vstLinPedidoVtaComisione linea)
        {
            string etiqueta;

            if (linea.Familia != null && linea.Familia.ToLower().Trim() == "lisap")
            {
                etiqueta = "Lisap";
            }
            else
            {
                etiqueta = "General";
            }
            return etiqueta;
        }

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            return (new ServicioComisionesAnualesComun()).LeerResumenAnno(NuevasEtiquetas, vendedor, anno);
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            vendedor = vendedor.ToUpper();

            return new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 50000M,
                    Tipo = .03M,
                    TipoExtra = .001M
                },new TramoComision
                {
                    Desde = 50000.01M,
                    Hasta = 52750M,
                    Tipo = .035M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 52750.01M,
                    Hasta = 63250M,
                    Tipo = .045M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 63250.01M,
                    Hasta = 66500M,
                    Tipo = .0671M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 66500.01M,
                    Hasta = 70250M,
                    Tipo = .072M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 70250.01M,
                    Hasta = 92100M,
                    Tipo = .077M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 92100.01M,
                    Hasta = 97200M,
                    Tipo = .08M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 97200.01M,
                    Hasta = 101700M,
                    Tipo = .085M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 101700.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0925M,
                    TipoExtra = .02M
                }
            };
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            return new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = decimal.MinValue,
                    Hasta = 2999.99M,
                    Tipo = 0,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 3000M,
                    Hasta = 5276.37M,
                    Tipo = .02M,
                    TipoExtra = 0
                }
            };
        }
    }
}