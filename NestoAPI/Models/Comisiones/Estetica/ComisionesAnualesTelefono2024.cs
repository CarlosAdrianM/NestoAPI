using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class ComisionesAnualesTelefono2024 : ComisionesAnualesBase, IComisionesAnuales
    {
        public ComisionesAnualesTelefono2024(IServicioComisionesAnuales servicioComisionesVentas) 
            : base(servicioComisionesVentas)
        {
            
        }

        public override ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
        {
            new EtiquetaGeneral(_servicio),
            new EtiquetaUnionLaser(_servicio),
            new EtiquetaFamiliasEspecialesEstado9(_servicio),
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
            else if (linea.Familia != null && EtiquetaFamiliasEspecialesEstado9.FamiliasIncluidas.Contains(linea.Familia.ToLower().Trim()))
            {
                etiqueta = "Familias Especiales Estado 9";
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

            Collection<TramoComision> tramosTelefono = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 49045.5M,
                    Tipo = .003M,
                    TipoExtra = .0002M
                },new TramoComision
                {
                    Desde = 49045.51M,
                    Hasta = 105583.17M,
                    Tipo = .006M,
                    TipoExtra = .0005M
                },new TramoComision
                {
                    Desde = 105583.18M,
                    Hasta = 111169.81M,
                    Tipo = .012M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 111169.82M,
                    Hasta = 132232.12M,
                    Tipo = .0145M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 132232.13M,
                    Hasta = 145392.67M,
                    Tipo = .0195M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 145392.67M,
                    Hasta = 158553.21M,
                    Tipo = .0225M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 158553.21M,
                    Hasta = 166700.21M,
                    Tipo = .0306M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 166700.24M,
                    Hasta = 176100.60M,
                    Tipo = .033M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 176100.61M,
                    Hasta = 192394.60M,
                    Tipo = .0355M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 192394.61M,
                    Hasta = 203048.38M,
                    Tipo = .0370M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 203048.39M,
                    Hasta = 212448.76M,
                    Tipo = .0395M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 212448.77M,
                    Hasta = 239846.06M,
                    Tipo = .042M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 239846.07M,
                    Hasta = 264853.71M,
                    Tipo = .045M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 264853.72M,
                    Hasta = 287587.93M,
                    Tipo = .055M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 287587.94M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0671M,
                    TipoExtra = .02M
                }
            };


            // Aquí usamos cadenas mágicas porque son los que YA han firmado las condiciones 
            if (vendedor == "AGR" || vendedor == "MPP" || vendedor == "PA" || vendedor == "KCP")
            {
                return tramosTelefono;
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