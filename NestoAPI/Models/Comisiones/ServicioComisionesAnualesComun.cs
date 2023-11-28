using NestoAPI.Infraestructure.Vendedores;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class ServicioComisionesAnualesComun : IServicioComisionesAnuales
    {
        const string GENERAL = "General";
        private readonly ServicioVendedores _servicioVendedores;

        public ServicioComisionesAnualesComun()
        {
            _servicioVendedores = new ServicioVendedores();
        }

        public NVEntities Db { get; } = new NVEntities();

        public decimal CalcularVentaFiltrada(bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta, ref IQueryable<vstLinPedidoVtaComisione> consulta, bool incluirPicking)
        {
            consulta = ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
            decimal venta = consulta.Select(l => l.Base_Imponible).DefaultIfEmpty().Sum();
            return venta;
        }

        public IQueryable<vstLinPedidoVtaComisione> ConsultaVentaFiltrada(bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta, ref IQueryable<vstLinPedidoVtaComisione> consulta, bool incluirPicking)
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

        public ICollection<ResumenComisionesMes> LeerResumenAnno(ICollection<IEtiquetaComision> etiquetas, string vendedor, int anno)
        {
            NVEntities db = new NVEntities();
            var listaVendedores = ListaVendedores(vendedor);
            var resumenDb = db.ComisionesAnualesResumenMes
                .Where(c => listaVendedores.Contains(c.Vendedor) && c.Anno == anno).OrderBy(r => r.Mes);

            if (resumenDb == null || resumenDb.Count() == 0)
            {
                return new Collection<ResumenComisionesMes>();
            }

            byte mesAnterior = resumenDb.First().Mes;

            ICollection<ResumenComisionesMes> resumenAnno = new Collection<ResumenComisionesMes>();
            ResumenComisionesMes resumenMes;
            try
            {
                resumenMes = new ResumenComisionesMes
                {
                    Vendedor = vendedor,
                    Anno = anno,
                    Mes = mesAnterior,
                    Etiquetas = etiquetas.Select(etiqueta => (IEtiquetaComision)etiqueta.Clone()).ToList()
                };
            } catch (Exception ex)
            {
                throw ex;
            }
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
                        Etiquetas = etiquetas.Select(etiqueta => (IEtiquetaComision)etiqueta.Clone()).ToList()
                        //Etiquetas = new List<IEtiquetaComision>(etiquetas.ToList()) // si esto funciona se puede eliminar el campo NuevasEtiquetas y usar siempre el campo Etiquetas.ToList()
                    };
                    mesAnterior = resumenMesDB.Mes;
                }

                try
                {
                    // si pasamos resumenMesDB por parámetro a la etiqueta y hacemos las asignaciones desde ahí, nos evitamos usar GENERAL
                    resumenMes.Etiquetas.Where(e => e.Nombre == resumenMesDB.Etiqueta).Single().Venta += resumenMesDB.Venta;
                    resumenMes.Etiquetas.Where(e => e.Nombre == resumenMesDB.Etiqueta).Single().Tipo += resumenMesDB.Tipo;
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

        public List<string>ListaVendedores(string vendedor)
        {
            return _servicioVendedores.VendedoresEquipo(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor).GetAwaiter().GetResult().Select(v => v.vendedor).ToList();
        }
    }
}