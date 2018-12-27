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
        private NVEntities db = new NVEntities();

        const string GENERAL = "General";

        // Refactorizar y quitar
        const string UNION_LASER= "Unión Láser";
        const string EVA_VISNU = "Eva Visnú";
        const string OTROS_APARATOS = "Otros Aparatos";

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
            List<VendedorDTO> vendedores = new List<VendedorDTO> {
                new VendedorDTO
                {
                    vendedor = "ASH"
                },
                new VendedorDTO
                {
                    vendedor = "DV"
                },
                new VendedorDTO
                {
                    vendedor = "JE"
                },
                new VendedorDTO
                {
                    vendedor = "JM"
                },
                new VendedorDTO
                {
                    vendedor = "MRM"
                },
                new VendedorDTO
                {
                    vendedor = "RFG"
                },
                new VendedorDTO
                {
                    vendedor = "CL"
                },
                new VendedorDTO
                {
                    vendedor = "LA"
                },
                new VendedorDTO
                {
                    vendedor = "PA"
                },
                new VendedorDTO
                {
                    vendedor = "SH"
                },
                new VendedorDTO
                {
                    vendedor = "AH"
                },
                new VendedorDTO
                {
                    vendedor = "IF"
                },
                new VendedorDTO
                {
                    vendedor = "JGP"
                }
            };
                        
            List<ResumenComisionesMes> comisiones = new List<ResumenComisionesMes>();

            foreach (VendedorDTO vendedor in vendedores)
            {
                VendedorComisionAnual vendedorComision = new VendedorComisionAnual(ServicioVendedor(vendedor.vendedor, anno), vendedor.vendedor, anno, mes);
                comisiones.Add(vendedorComision.ResumenMesActual);
            }

            return comisiones;
        }

        //private DateTime FechaDesde(int anno, int mes)
        //{
        //    return new DateTime(anno, mes, 1);
        //}

        //public static DateTime FechaHasta(int anno, int mes)
        //{
        //    if (mes != 12)
        //    {
        //        return (new DateTime(anno, mes + 1, 1)).AddDays(-1);
        //    }
        //    else
        //    {
        //        return (new DateTime(anno + 1, 1, 1)).AddDays(-1);
        //    }
        //}

        private IServicioComisionesAnuales ServicioVendedor(string vendedor, int anno)
        {
            if (vendedor == "IF" || vendedor == "AH" || vendedor == "JGP")
            {
                return new ServicioComisionesAnualesPeluqueria2018();
            }

            return new ServicioComisionesAnualesEstetica2018();
        }
    }
}