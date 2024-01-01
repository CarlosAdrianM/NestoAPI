using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models.Comisiones.Estetica;
using NestoAPI.Models.Comisiones.Peluqueria;
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

        public List<ClienteVenta> LeerClientesConVenta(string vendedor, int anno, int mes)
        {
            var fechaDesde = VendedorComisionAnual.FechaDesde(anno, 1); // desde enero
            var fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            using (var db = new NVEntities())
            {
                var query = from c in db.Clientes
                            join l in db.LinPedidoVtas on new { c.Nº_Cliente, c.Contacto } equals new { l.Nº_Cliente, l.Contacto }
                            join v in db.VendedoresLinPedidoVta on l.Nº_Orden equals v.Id
                            where c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && (l.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || l.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) &&
                                  v.Vendedor == vendedor &&
                                  l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta                                  
                            group l by new { c.Nº_Cliente } into g
                            select new ClienteVenta
                            {
                                Cliente = g.Key.Nº_Cliente,
                                Venta = g.Sum(l => l.Base_Imponible)
                            };

                return query.ToList();
            }
        }

        public List<ClienteVenta> LeerClientesNuevosConVenta(string vendedor, int anno, int mes)
        {
            var fechaDesde = VendedorComisionAnual.FechaDesde(anno, 1); // desde enero
            var fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            using (var db = new NVEntities())
            {
                var query = from c in db.Clientes
                            join l in db.LinPedidoVtas on new { c.Nº_Cliente, c.Contacto } equals new { l.Nº_Cliente, l.Contacto }
                            join v in db.VendedoresLinPedidoVta on l.Nº_Orden equals v.Id
                            where c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && (l.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || l.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && 
                                  v.Vendedor == vendedor &&
                                  l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta &&
                                  !(db.LinPedidoVtas.Any(l2 => l2.Nº_Cliente == l.Nº_Cliente && l2.Fecha_Factura < fechaDesde))
                            group l by new { c.Nº_Cliente } into g
                            select new ClienteVenta
                            {
                                Cliente = g.Key.Nº_Cliente,
                                Venta = g.Sum(l => l.Base_Imponible)
                            };

                return query.ToList();
            }
        }

        public List<ComisionAnualResumenMes> LeerComisionesAnualesResumenMes(List<string> listaVendedores, int anno)
        {
            using (var db = new NVEntities())
            {
                return db.ComisionesAnualesResumenMes
                .Where(c => listaVendedores.Contains(c.Vendedor) && c.Anno == anno).OrderBy(r => r.Mes).ToList();
            }
        }

        public List<string>ListaVendedores(string vendedor)
        {
            return _servicioVendedores.VendedoresEquipo(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor).GetAwaiter().GetResult().Select(v => v.vendedor).ToList();
        }

        public static IComisionesAnuales ComisionesVendedor(string vendedor, int anno)
        {
            using (var db = new NVEntities())
            {
                var vendedoresPeluqueria = db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_PELUQUERIA).Select(v => v.Número.Trim());

                if (vendedoresPeluqueria.Contains(vendedor))
                {
                    if (anno == 2018)
                    {
                        return new ComisionesAnualesPeluqueria2018();
                    }
                    else if (anno == 2019)
                    {
                        return new ComisionesAnualesPeluqueria2019();
                    }
                    else if (anno == 2020)
                    {
                        return new ComisionesAnualesPeluqueria2020();
                    }
                    else if (anno == 2021 || anno == 2022 || anno == 2023)
                    {
                        return new ComisionesAnualesPeluqueria2021(new ServicioComisionesAnualesComun());
                    }
                    else if (anno == 2024)
                    {
                        return new ComisionesAnualesPeluqueria2024(new ServicioComisionesAnualesComun());
                    }
                }

                var vendedoresTelefono = db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO).Select(v => v.Número.Trim());

                if (vendedoresTelefono.Contains(vendedor))
                {
                    if (anno == 2018)
                    {
                        return new ComisionesAnualesEstetica2018();
                    }
                    else if (anno == 2019)
                    {
                        return new ComisionesAnualesEstetica2019();
                    }
                    else if (anno == 2020)
                    {
                        return new ComisionesAnualesEstetica2020();
                    }
                    else if (anno == 2021)
                    {
                        return new ComisionesAnualesEstetica2021();
                    }
                    else if (anno == 2022 || anno == 2023)
                    {
                        return new ComisionesAnualesEstetica2022(new ServicioComisionesAnualesComun());
                    }
                    else if (anno == 2024)
                    {
                        return new ComisionesAnualesTelefono2024(new ServicioComisionesAnualesComun());
                    }
                }

                var jefesVentas = db.EquiposVentas.Where(e => e.Superior == vendedor && e.FechaDesde <= new DateTime(anno, 1, 1)).Select(e => e.Superior).Distinct().ToList();

                if (jefesVentas.Contains(vendedor))
                {
                    if (anno == 2024)
                    {
                        return new ComisionesAnualesJefeVentas2024(new ServicioComisionesAnualesComun());
                    }
                }

                if (anno == 2018)
                {
                    return new ComisionesAnualesEstetica2018();
                }
                else if (anno == 2019)
                {
                    return new ComisionesAnualesEstetica2019();
                }
                else if (anno == 2020)
                {
                    return new ComisionesAnualesEstetica2020();
                }
                else if (anno == 2021)
                {
                    return new ComisionesAnualesEstetica2021();
                }
                else if (anno == 2022 || anno == 2023)
                {
                    return new ComisionesAnualesEstetica2022(new ServicioComisionesAnualesComun());
                }
                else if (anno == 2024)
                {
                    return new ComisionesAnualesEstetica2024(new ServicioComisionesAnualesComun());
                }

                throw new Exception("El año " + anno.ToString() + " no está controlado por el sistema de comisiones");
            }
        }
    }
}