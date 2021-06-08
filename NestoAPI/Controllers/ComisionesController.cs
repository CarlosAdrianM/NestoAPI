using NestoAPI.Models;
using NestoAPI.Models.Comisiones;
using NestoAPI.Models.Comisiones.Peluqueria;
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
        private readonly NVEntities db = new NVEntities();

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
            VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioVendedor(vendedor, anno), vendedor, anno, mes);
            return Ok(vendedorComision.ResumenMesActual);
        }


        // GET: api/Comisiones
        [HttpGet]
        [ResponseType(typeof(ResumenComisionesMes))]
        public async Task<IHttpActionResult> GetComisiones(string vendedor, int anno)
        {
            int mes = DateTime.Today.Month;
            VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioVendedor(vendedor, anno), vendedor, anno, mes);
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
                VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioVendedor(vendedor, anno), vendedor, anno, mes);
                return Ok(vendedorComision.ResumenMesActual);
            } else
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
            VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioVendedor(vendedor, anno), vendedor, anno, mes, incluirAlbaranes);
            
            //await Task.Run(() => vendedor = new VendedorComisionAnual(servicio, "PA", 2018));

            return Ok(vendedorComision.ResumenMesActual);
        }

        // GET: api/Comisiones
        [HttpGet]
        [ResponseType(typeof(ResumenComisionesMes))]
        public async Task<IHttpActionResult> GetComisiones(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioVendedor(vendedor, anno), vendedor, anno, mes, incluirAlbaranes, incluirPicking);

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
                        db.ComisionesAnualesResumenMes.Add(new ComisionAnualResumenMes
                        {
                            Vendedor = resumen.Vendedor,
                            Anno = (short)resumen.Anno,
                            Mes = (byte)resumen.Mes,
                            Etiqueta = etiqueta.Nombre,
                            Venta = etiqueta.Venta,
                            Tipo = etiqueta.Tipo,
                            Comision = etiqueta.Comision
                        });
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }

                var lineas = db.vstLinPedidoVtaComisiones
                    .Where(l => l.Vendedor == resumen.Vendedor &&
                    l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta
                    && l.Grupo != null && l.Familia != null);

                foreach (vstLinPedidoVtaComisione linea in lineas)
                {                  

                    string etiqueta;

                    try
                    {
                        // Arreglar sin cadenas mágicas
                        if (linea.Grupo != null && linea.Grupo.ToLower().Trim() == "otros aparatos")
                        {
                            etiqueta = "Otros Aparatos";
                        }
                        else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "uniónláser")
                        {
                            etiqueta = "Unión Láser";
                        }
                        else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "eva visnu")
                        {
                            etiqueta = "Eva Visnú";
                        }
                        else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "lisap")
                        {
                            etiqueta = "Lisap";
                        }
                        else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "kach")
                        {
                            etiqueta = "Kach";
                        }
                        else
                        {
                            etiqueta = "General";
                        }
                        db.ComisionesAnualesDetalles.Add(new ComisionAnualDetalle
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
                    } catch (Exception ex)
                    {
                        throw ex;
                    }
                }
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                throw;
            }

            return Ok(comisiones);
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
                Select(v => new VendedorDTO {
                    vendedor = v.Número.Trim()
                }).ToList();
    
            List<ResumenComisionesMes> comisiones = new List<ResumenComisionesMes>();

            foreach (VendedorDTO vendedor in vendedores)
            {
                VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioVendedor(vendedor.vendedor, anno), vendedor.vendedor, anno, mes);
                comisiones.Add(vendedorComision.ResumenMesActual);
            }

            return comisiones;
        }

        private IServicioComisionesAnuales ServicioVendedor(string vendedor, int anno)
        {
            var vendedoresPeluqueria = db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_PELUQUERIA).Select(v => v.Número.Trim());
            
            if (vendedoresPeluqueria.Contains(vendedor))
            {
                if (anno == 2018)
                {
                    return new ServicioComisionesAnualesPeluqueria2018();
                }
                else if (anno == 2019)
                {
                    return new ServicioComisionesAnualesPeluqueria2019();
                }
                else if (anno == 2020)
                {
                    return new ServicioComisionesAnualesPeluqueria2020();
                }
                else if (anno == 2021)
                {
                    return new ServicioComisionesAnualesPeluqueria2021();
                }
            }

            if (anno == 2018)
            {
                return new ServicioComisionesAnualesEstetica2018();
            } 
            else if (anno == 2019)
            {
                return new ServicioComisionesAnualesEstetica2019();
            }
            else if (anno == 2020)
            {
                return new ServicioComisionesAnualesEstetica2020();
            }
            else if (anno == 2021)
            {
                return new ServicioComisionesAnualesEstetica2021();
            }

            throw new Exception("El año " + anno.ToString() + " no está controlado por el sistema de comisiones");
        }
    }
}