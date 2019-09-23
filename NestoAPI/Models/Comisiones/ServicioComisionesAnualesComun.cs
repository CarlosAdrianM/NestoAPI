using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class ServicioComisionesAnualesComun
    {
        const string GENERAL = "General";

        public static decimal CalcularVentaFiltrada(bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta, ref IQueryable<vstLinPedidoVtaComisione> consulta, bool incluirPicking)
        {
            consulta = ServicioComisionesAnualesComun.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
            decimal venta = consulta.Select(l => l.Base_Imponible).DefaultIfEmpty().Sum();
            return venta;
        }

        public static IQueryable<vstLinPedidoVtaComisione> ConsultaVentaFiltrada(bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta, ref IQueryable<vstLinPedidoVtaComisione> consulta, bool incluirPicking)
        {
            if (consulta == null)
            {
                return null;
            }
            if (incluirPicking && incluirAlbaranes)
            {
                consulta = consulta.Where(l => (l.Estado == 1 && l.Picking>0) || l.Estado == 2 || (l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4));
            }
            else if (incluirAlbaranes)
            {
                consulta = consulta.Where(l => l.Estado == 2 || (l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4));
            }
            else if (incluirPicking)
            {
                consulta = consulta.Where(l => (l.Estado == 1 && l.Picking > 0) || (l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4));
            }
            else
            {
                consulta = consulta.Where(l => l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4);
            }
            return consulta;
        }

        public static ICollection<ResumenComisionesMes> LeerResumenAnno(IServicioComisionesAnuales servicio, string vendedor, int anno)
        {
            NVEntities db = new NVEntities();
            var resumenDb = db.ComisionesAnualesResumenMes
                .Where(c => c.Vendedor == vendedor && c.Anno == anno).OrderBy(r => r.Mes);

            if (resumenDb == null || resumenDb.Count() == 0)
            {
                return new Collection<ResumenComisionesMes>();
            }

            byte mesAnterior = resumenDb.First().Mes;

            ICollection<ResumenComisionesMes> resumenAnno = new Collection<ResumenComisionesMes>();
            ResumenComisionesMes resumenMes = new ResumenComisionesMes
            {
                Vendedor = vendedor,
                Anno = anno,
                Mes = mesAnterior,
                Etiquetas = servicio.NuevasEtiquetas
            };
            foreach (ComisionAnualResumenMes resumenMesDB in resumenDb)
            {
                if (mesAnterior != resumenMesDB.Mes)
                {
                    resumenAnno.Add(resumenMes);
                    resumenMes = new ResumenComisionesMes
                    {
                        Vendedor = resumenMesDB.Vendedor,
                        Anno = resumenMesDB.Anno,
                        Mes = resumenMesDB.Mes,
                        Etiquetas = servicio.NuevasEtiquetas
                    };
                    mesAnterior = resumenMesDB.Mes;
                }

                try
                {
                    // si pasamos resumenMesDB por parámetro a la etiqueta y hacemos las asignaciones desde ahí, nos evitamos usar GENERAL
                    resumenMes.Etiquetas.Where(e => e.Nombre == resumenMesDB.Etiqueta).Single().Venta = resumenMesDB.Venta;
                    resumenMes.Etiquetas.Where(e => e.Nombre == resumenMesDB.Etiqueta).Single().Tipo = resumenMesDB.Tipo;
                    if (resumenMesDB.Etiqueta == GENERAL)
                    {
                        resumenMes.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision = resumenMesDB.Comision;
                    }
                }
                catch
                {
                    Console.WriteLine("Etiqueta no válida en la tabla de resúmenes de comisiones del vendedor " + resumenMesDB.Vendedor);
                }

            }
            resumenAnno.Add(resumenMes);

            return resumenAnno;
        }
    }
}