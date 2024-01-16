using NestoAPI.Models.Comisiones.Estetica;
using NestoAPI.Models.Comisiones.Estetica.Etiquetas;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnualesEstetica2024 : ComisionesAnualesBase, IComisionesAnuales
    {
        public ComisionesAnualesEstetica2024(IServicioComisionesAnuales servicioComisiones)
            : base(servicioComisiones)
        {
            
        }
                
        public override ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
        {
            new EtiquetaGeneral(_servicio),
            new EtiquetaUnionLaser(_servicio),
            new EtiquetaFamiliasEspeciales(_servicio),
            new EtiquetaOtrasExclusivas(_servicio),
            new EtiquetaOtrosAparatos(_servicio),
            new EtiquetaClientesNuevos(_servicio),
            new EtiquetaClientesTramosMil(_servicio)
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
        //public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        //{
        //    return LeerResumenAnno(NuevasEtiquetas, vendedor, anno);
        //}

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            vendedor = vendedor.ToUpper();
            Collection<TramoComision> tramosCalle = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 33350.94M,
                    Tipo = .006M,
                    TipoExtra = .0105M
                },
                new TramoComision
                {
                    Desde = 33350.95M,
                    Hasta = 45000M,
                    Tipo = .02M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 45000.01M,
                    Hasta = 58000M,
                    Tipo = .0205M,
                    TipoExtra = .0115M
                },
                new TramoComision
                {
                    Desde = 58000.01M,
                    Hasta = 73372.07M,
                    Tipo = .021M,
                    TipoExtra = .012M
                },
                new TramoComision
                {
                    Desde = 73372.08M,
                    Hasta = 94494.33M,
                    Tipo = .023M,
                    TipoExtra = .0125M
                },
                new TramoComision
                {
                    Desde = 94494.34M,
                    Hasta = 111169.80M,
                    Tipo = .025M,
                    TipoExtra = .013M
                },
                new TramoComision
                {
                    Desde = 111169.81M,
                    Hasta = 127250M,
                    Tipo = .027M,
                    TipoExtra = .0135M
                },
                new TramoComision
                {
                    Desde = 127250.01M,
                    Hasta = 144520.74M,
                    Tipo = .0275M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 144520.75M,
                    Hasta = 162500M,
                    Tipo = .028M,
                    TipoExtra = .0145M
                },
                new TramoComision
                {
                    Desde = 162500.01M,
                    Hasta = 183430.17M,
                    Tipo = .0285M,
                    TipoExtra = .015M
                },
                new TramoComision
                {
                    Desde = 183430.18M,
                    Hasta = 199000M,
                    Tipo = .029M,
                    TipoExtra = .0155M
                },
                new TramoComision
                {
                    Desde = 199000.01M,
                    Hasta = 215669.41M,
                    Tipo = .0295M,
                    TipoExtra = .016M
                },
                new TramoComision
                {
                    Desde = 215669.42M,
                    Hasta = 227342.24M,
                    Tipo = .03M,
                    TipoExtra = .0165M
                },
                new TramoComision
                {
                    Desde = 227342.25M,
                    Hasta = 239846.06M,
                    Tipo = .035M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 239846.07M,
                    Hasta = 250000M,
                    Tipo = .045M,
                    TipoExtra = .0175M
                },
                new TramoComision
                {
                    Desde = 250000.01M,
                    Hasta = 264853.71M,
                    Tipo = .05M,
                    TipoExtra = .018M
                },
                new TramoComision
                {
                    Desde = 264853.72M,
                    Hasta = 287587.93M,
                    Tipo = .055M,
                    TipoExtra = .0185M
                },
                new TramoComision
                {
                    Desde = 287587.94M,
                    Hasta = 302365.18M,
                    Tipo = .0671M,
                    TipoExtra = .019M
                },
                new TramoComision
                {
                    Desde = 302365.19M,
                    Hasta = 319415.85M,
                    Tipo = .072M,
                    TipoExtra = .0195M
                },
                new TramoComision
                {
                    Desde = 319415.86M,
                    Hasta = 330000M,
                    Tipo = .077M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 330000.01M,
                    Hasta = 348970.34M,
                    Tipo = .0785M,
                    TipoExtra = .021M
                },
                new TramoComision
                {
                    Desde = 348970.35M,
                    Hasta = 368294.43M,
                    Tipo = .08M,
                    TipoExtra = .024M
                },
                new TramoComision
                {
                    Desde = 368294.44M,
                    Hasta = 385345.10M,
                    Tipo = .085M,
                    TipoExtra = .027M
                },
                new TramoComision
                {
                    Desde = 385345.11M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0925M,
                    TipoExtra = .03M
                }
            };

            Collection<TramoComision> tramosMinivendedores = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 5250M,
                    Tipo = .0M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 5250.01M,
                    Hasta = 10500M,
                    Tipo = .029M,
                    TipoExtra = .0005M
                },
                new TramoComision
                {
                    Desde = 10500.01M,
                    Hasta = 15750M,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 15750.01M,
                    Hasta = 21000M,
                    Tipo = .035M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 21000.01M,
                    Hasta = 36750M,
                    Tipo = .045M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 36750.01M,
                    Hasta = 47250M,
                    Tipo = .055M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 47250.01M,
                    Hasta = 57750M,
                    Tipo = .0671M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 57750.01M,
                    Hasta = 68250M,
                    Tipo = .072M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 68250.01M,
                    Hasta = 78750M,
                    Tipo = .077M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 78750.01M,
                    Hasta = 89250M,
                    Tipo = .08M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 89250.01M,
                    Hasta = 99750M,
                    Tipo = .085M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 99750.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0925M,
                    TipoExtra = .02M
                }
            };            

            // Aquí usamos cadenas mágicas porque son los que YA han firmado las condiciones 
            if (vendedor == "DV" || vendedor == "JE" || vendedor == "RFG" || vendedor == "IM" || vendedor == "JGP" || vendedor == "MRM" || vendedor == "RAS")
            {
                return tramosCalle;
            }
            else if (vendedor == "AL" || vendedor == "CAM" || vendedor == "MR" || vendedor == "PI" || vendedor == "SC" || vendedor == "LC")
            {
                return tramosMinivendedores;
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