using NestoAPI.Infraestructure.Comisiones;
using NestoAPI.Models;
using NestoAPI.Models.Comisiones;
using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class ComisionesController : ApiController
    {
        private readonly NVEntities db;
        private readonly IComisionesLecturaService _lectura;

        public ComisionesController()
        {
            db = new NVEntities();
            _lectura = new ComisionesLecturaService(db);
        }

        public ComisionesController(IComisionesLecturaService lectura)
        {
            db = new NVEntities();
            _lectura = lectura;
        }

        // GET: api/Comisiones
        [HttpGet]
        [ResponseType(typeof(List<ResumenComisionesMes>))]
        public async Task<IHttpActionResult> GetComisiones()
        {
            List<ResumenComisionesMes> comisiones = CalcularComisiones();
            return Ok(comisiones);
        }

        // GET: api/Comisiones
        [HttpGet]
        [ResponseType(typeof(ResumenComisionesMes))]
        public async Task<IHttpActionResult> GetComisiones(string vendedor)
        {
            int anno = DateTime.Today.Year;
            int mes = DateTime.Today.Month;
            VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioSelectorTipoComisionesAnualesVendedor.ComisionesVendedor(vendedor, anno, mes), vendedor, anno, mes);
            return Ok(vendedorComision.ResumenMesActual);
        }


        // GET: api/Comisiones
        [HttpGet]
        [ResponseType(typeof(ResumenComisionesMes))]
        public async Task<IHttpActionResult> GetComisiones(string vendedor, int anno)
        {
            int mes = DateTime.Today.Month;
            VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioSelectorTipoComisionesAnualesVendedor.ComisionesVendedor(vendedor, anno, mes), vendedor, anno, mes);
            return Ok(vendedorComision.ResumenMesActual);
        }

        // GET: api/Comisiones
        [HttpGet]
        [ResponseType(typeof(ResumenComisionesMes))]
        public async Task<IHttpActionResult> GetComisiones(string vendedor, int anno, int mes)
        {
            // si pasamos el vendedor en blanco, sacamos todos
            if (vendedor != null)
            {
                VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioSelectorTipoComisionesAnualesVendedor.ComisionesVendedor(vendedor, anno, mes), vendedor, anno, mes);
                return Ok(vendedorComision.ResumenMesActual);
            }
            else
            {
                List<ResumenComisionesMes> comisiones = CalcularComisiones(anno, mes);
                return Ok(comisiones);
            }
        }

        // GET: api/Comisiones
        [HttpGet]
        [ResponseType(typeof(ResumenComisionesMes))]
        public async Task<IHttpActionResult> GetComisiones(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioSelectorTipoComisionesAnualesVendedor.ComisionesVendedor(vendedor, anno, mes), vendedor, anno, mes, incluirAlbaranes);

            //await Task.Run(() => vendedor = new VendedorComisionAnual(servicio, "PA", 2018));

            return Ok(vendedorComision.ResumenMesActual);
        }

        // GET: api/Comisiones
        [HttpGet]
        [ResponseType(typeof(ResumenComisionesMes))]
        public async Task<IHttpActionResult> GetComisiones(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            var tipoComision = ServicioSelectorTipoComisionesAnualesVendedor.ComisionesVendedor(vendedor, anno, mes);
            VendedorComisionAnual vendedorComision = new VendedorComisionAnual(tipoComision, vendedor, anno, mes, incluirAlbaranes, incluirPicking);

            //await Task.Run(() => vendedor = new VendedorComisionAnual(servicio, "PA", 2018));

            return Ok(vendedorComision.ResumenMesActual);
        }

        // POST: api/Comisiones
        [ResponseType(typeof(List<ResumenComisionesMes>))]
        public async Task<IHttpActionResult> PostComisiones(int anno, int mes)
        {
            List<ResumenComisionesMes> comisiones = CalcularComisiones(anno, mes);
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            if (fechaDesde < new DateTime(2018, 1, 1))
            {
                throw new Exception("Las comisiones anuales entraron en vigor el 01/01/18");
            }
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            foreach (ResumenComisionesMes resumen in comisiones)
            {
                foreach (IEtiquetaComision etiqueta in resumen.Etiquetas)
                {
                    try
                    {
                        if (etiqueta is IEtiquetaComisionVenta)
                        {
                            _ = db.ComisionesAnualesResumenMes.Add(new ComisionAnualResumenMes
                            {
                                Vendedor = resumen.Vendedor,
                                Anno = (short)resumen.Anno,
                                Mes = (byte)resumen.Mes,
                                Etiqueta = etiqueta.Nombre,
                                Venta = (etiqueta as IEtiquetaComisionVenta).Venta,
                                Tipo = etiqueta.Tipo,
                                Comision = etiqueta.Comision
                            });
                        }
                        else if (etiqueta is IEtiquetaComisionClientes)
                        {
                            _ = db.ComisionesAnualesResumenMes.Add(new ComisionAnualResumenMes
                            {
                                Vendedor = resumen.Vendedor,
                                Anno = (short)resumen.Anno,
                                Mes = (byte)resumen.Mes,
                                Etiqueta = etiqueta.Nombre,
                                Venta = (etiqueta as IEtiquetaComisionClientes).Recuento, // Se almacena en la misma tabla porque es decimal
                                Tipo = etiqueta.Tipo,
                                Comision = etiqueta.Comision
                            });
                        }
                        else
                        {
                            throw new Exception("Tipo de etiqueta no contemplado");
                        }

                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }


                // Carlos 29/12/23: esta parte de abajo solo sirve para IEtiquetaComisionVenta, hay que escribir la de clientes
                var lineas = db.vstLinPedidoVtaComisiones
                    .Where(l => l.Vendedor == resumen.Vendedor &&
                    l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta
                    && l.Grupo != null && l.Familia != null);

                foreach (vstLinPedidoVtaComisione linea in lineas)
                {
                    try
                    {
                        string etiqueta = ServicioSelectorTipoComisionesAnualesVendedor.ComisionesVendedor(linea.Vendedor, anno, mes).EtiquetaLinea(linea);

                        _ = db.ComisionesAnualesDetalles.Add(new ComisionAnualDetalle
                        {
                            Id = linea.Nº_Orden,
                            EstadoFamilia = (short)linea.EstadoFamilia,
                            Pedido = linea.Número,
                            BaseImponible = linea.Base_Imponible,
                            Vendedor = linea.Vendedor,
                            Anno = (short)anno,
                            Mes = (byte)mes,
                            Etiqueta = etiqueta
                        });
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }
            }

            try
            {
                _ = await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return Ok(comisiones);
        }

        // Nesto#340 Fase 1B: lecturas para el panel de Comisiones del cliente Nesto.

        [HttpGet]
        [Route("api/Comisiones/Antiguas")]
        [ResponseType(typeof(ComisionesAntiguasDTO))]
        public async Task<IHttpActionResult> GetComisionesAntiguas(
            DateTime fechaDesde, DateTime fechaHasta, string vendedor, string empresa = "1")
        {
            var resultado = await _lectura
                .LeerComisionesAntiguasAsync(empresa, fechaDesde, fechaHasta, vendedor)
                .ConfigureAwait(false);

            if (resultado == null) return NotFound();
            return Ok(resultado);
        }

        [HttpGet]
        [Route("api/Comisiones/PedidosVendedor")]
        [ResponseType(typeof(List<PedidoVendedorComisionDTO>))]
        public async Task<IHttpActionResult> GetPedidosVendedor(string vendedor)
        {
            var lista = await _lectura
                .LeerPedidosVendedorAsync(vendedor)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        [HttpGet]
        [Route("api/Comisiones/VentasVendedor")]
        [ResponseType(typeof(List<VentaVendedorComisionDTO>))]
        public async Task<IHttpActionResult> GetVentasVendedor(
            DateTime fechaDesde, DateTime fechaHasta, string vendedor)
        {
            var lista = await _lectura
                .LeerVentasVendedorAsync(fechaDesde, fechaHasta, vendedor)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        private List<ResumenComisionesMes> CalcularComisiones()
        {
            int mes = DateTime.Today.Month; // ojo, que al cerrar el mes hay que coger el anterior
            int anno = DateTime.Today.Year;
            return CalcularComisiones(anno, mes);
        }

        private List<ResumenComisionesMes> CalcularComisiones(int anno, int mes)
        {
            List<VendedorDTO> vendedores = db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Número != Constantes.Vendedores.VENDEDOR_GENERAL &&
                v.Estado >= Constantes.Vendedores.ESTADO_VENDEDOR_PRESENCIAL && v.Estado < Constantes.Vendedores.ESTADO_VENDEDOR_PARA_ANULAR).
                Select(v => new VendedorDTO
                {
                    vendedor = v.Número.Trim()
                }).ToList();

            List<ResumenComisionesMes> comisiones = new List<ResumenComisionesMes>();

            foreach (VendedorDTO vendedor in vendedores)
            {
                VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioSelectorTipoComisionesAnualesVendedor.ComisionesVendedor(vendedor.vendedor, anno, mes), vendedor.vendedor, anno, mes);
                comisiones.Add(vendedorComision.ResumenMesActual);
            }

            return comisiones;
        }
    }
}