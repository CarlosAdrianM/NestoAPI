using NestoAPI.Models.Comisiones.Estetica;
using NestoAPI.Models.Comisiones.Estetica.Etiquetas;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnualesJefeVentas2024 : ComisionesAnualesBase, IComisionesAnuales
    {
        public ComisionesAnualesJefeVentas2024(IServicioComisionesAnuales servicioComisiones)
            : base(servicioComisiones)
        {

        }

        public override ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
        {
            new EtiquetaGeneral(_servicio),
            new EtiquetaUnionLaserJefeVentas(_servicio),
            new EtiquetaFamiliasEspeciales(_servicio),
            new EtiquetaOtrasExclusivas(_servicio),
            new EtiquetaOtrosAparatos(_servicio)
        };

        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019(this);

        public string EtiquetaLinea(vstLinPedidoVtaComisione linea)
        {
            string etiqueta;

            if (linea.Grupo != null && linea.Grupo.ToLower().Trim() == "otros aparatos")
            {
                etiqueta = "Otros Aparatos";
            }
            else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "uniónláser")
            {
                etiqueta = "Unión Láser";
            }
            else if (linea.Familia != null && EtiquetaFamiliasEspeciales.FamiliasIncluidas.Contains(linea.Familia.ToLower().Trim()))
            {
                etiqueta = "Familias Especiales";
            }
            else if (linea.Familia != null && EtiquetaOtrasExclusivas.FamiliasIncluidas.Contains(linea.Familia.ToLower().Trim()))
            {
                etiqueta = "Otras Exclusivas";
            }
            else
            {
                etiqueta = "General";
            }
            return etiqueta;
        }
        
        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            vendedor = vendedor.ToUpper();

            Collection<TramoComision> tramosJefeDeVentas = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 600000M,
                    Tipo = .003M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 600000.01M,
                    Hasta = 700000M,
                    Tipo = .004M,
                    TipoExtra = .0005M
                },
                new TramoComision
                {
                    Desde = 700000.01M,
                    Hasta = 800000M,
                    Tipo = .005M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 800000.01M,
                    Hasta = 900000M,
                    Tipo = .006M,
                    TipoExtra = .0015M
                },
                new TramoComision
                {
                    Desde = 900000.01M,
                    Hasta = 1000000M,
                    Tipo = .007M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 1000000.01M,
                    Hasta = 1100000M,
                    Tipo = .008M,
                    TipoExtra = .0025M
                },
                new TramoComision
                {
                    Desde = 1100000.01M,
                    Hasta = 1200000M,
                    Tipo = .009M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 1200000.01M,
                    Hasta = 1300000M,
                    Tipo = .01M,
                    TipoExtra = .0035M
                },
                new TramoComision
                {
                    Desde = 1300000.01M,
                    Hasta = 1400000M,
                    Tipo = .011M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 1400000.01M,
                    Hasta = 1500000M,
                    Tipo = .012M,
                    TipoExtra = .0045M
                },
                new TramoComision
                {
                    Desde = 1500000.01M,
                    Hasta = 1600000M,
                    Tipo = .013M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 1600000.01M,
                    Hasta = 1700000M,
                    Tipo = .014M,
                    TipoExtra = .0055M
                },
                new TramoComision
                {
                    Desde = 1700000.01M,
                    Hasta = 1800000M,
                    Tipo = .015M,
                    TipoExtra = .006M
                },
                new TramoComision
                {
                    Desde = 1800000.01M,
                    Hasta = 1900000M,
                    Tipo = .016M,
                    TipoExtra = .0065M
                },
                new TramoComision
                {
                    Desde = 1900000.01M,
                    Hasta = 2000000M,
                    Tipo = .017M,
                    TipoExtra = .007M
                },
                new TramoComision
                {
                    Desde = 2000000.01M,
                    Hasta = 2100000M,
                    Tipo = .018M,
                    TipoExtra = .0075M
                },
                new TramoComision
                {
                    Desde = 2100000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .019M,
                    TipoExtra = .008M
                }
            };

            // Aquí usamos cadenas mágicas porque son los que YA han firmado las condiciones 
            if (vendedor == "ASH")
            {
                return tramosJefeDeVentas;
            }

            throw new Exception("El vendedor " + vendedor + " no comisiona por este esquema");
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            //Este año no hay tramos mensuales
            return new Collection<TramoComision>();
        }

    }
}