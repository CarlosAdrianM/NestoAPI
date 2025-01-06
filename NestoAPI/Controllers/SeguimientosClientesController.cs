using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Models;
using System.Data.Entity.Core.Objects;
using NestoAPI.Models.Rapports;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.Vendedores;
using Newtonsoft.Json;
using System.Text;
using System.Configuration;

namespace NestoAPI.Controllers
{
    public class SeguimientosClientesController : ApiController
    {
        private NVEntities db = new NVEntities();

        // Carlos 13/03/17: lo pongo para desactivar el Lazy Loading
        public SeguimientosClientesController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        // GET: api/SeguimientosClientes
        public IQueryable<SeguimientoClienteDTO> GetSeguimientosClientes(string empresa, string cliente, string contacto)
        {
            DateTime fechaDesde = DateTime.Today.AddYears(-3);
            return db.SeguimientosClientes
                .Include(s => s.Cliente)
                .Where(s => s.Empresa == empresa && s.Número == cliente && s.Contacto == contacto && s.Fecha >= fechaDesde)
                .Select(s => new SeguimientoClienteDTO {
                    Aparatos = s.Aparatos,
                    Aviso = s.Aviso,
                    Cliente = s.Número.Trim(),
                    ClienteNuevo = s.ClienteNuevo,
                    Comentarios = s.Comentarios,
                    Contacto = s.Contacto.Trim(),
                    Direccion = s.Cliente.Dirección,
                    Empresa = s.Empresa.Trim(),
                    Estado = (SeguimientoClienteDTO.EstadoSeguimientoDTO)s.Estado,
                    Fecha = s.Fecha,
                    GestionAparatos = s.GestiónAparatos,
                    Id = s.NºOrden,
                    Nombre = s.Cliente.Nombre,
                    Pedido = s.Pedido,
                    PrimeraVisita = s.PrimeraVisita,
                    Tipo = s.Tipo.Trim(),
                    Usuario = s.Usuario,
                    Vendedor = s.Vendedor
                })
                .OrderByDescending(s=> s.Id);
        }

        // GET: api/SeguimientosClientes
        public IQueryable<SeguimientoClienteDTO> GetSeguimientosClientes(string vendedor, DateTime fecha)
        {
            DateTime fechaDesde = new DateTime(fecha.Year, fecha.Month, fecha.Day);
            DateTime fechaHasta = fechaDesde.AddDays(1);

            IQueryable<SeguimientoCliente> seguimientos;

            if (vendedor != null)
            {
                if (vendedor.Length <= 3)
                {
                    seguimientos = db.SeguimientosClientes.Where(s => s.Vendedor == vendedor);
                }
                else
                {
                    seguimientos = db.SeguimientosClientes.Where(s => s.Usuario == vendedor);
                }
            } else
            {
                seguimientos = db.SeguimientosClientes;
            }
            

            return seguimientos
                .Include(s => s.Cliente)
                .Where(s => s.Fecha >= fechaDesde && s.Fecha < fechaHasta)
                .Select(s => new SeguimientoClienteDTO
                {
                    Aparatos = s.Aparatos,
                    Aviso = s.Aviso,
                    Cliente = s.Número,
                    ClienteNuevo = s.ClienteNuevo,
                    Comentarios = s.Comentarios,
                    Contacto = s.Contacto,
                    Direccion = s.Cliente.Dirección,
                    Empresa = s.Empresa,
                    Estado = (SeguimientoClienteDTO.EstadoSeguimientoDTO)s.Estado,
                    Fecha = s.Fecha,
                    GestionAparatos = s.GestiónAparatos,
                    Id = s.NºOrden,
                    Nombre = s.Cliente.Nombre,
                    Pedido = s.Pedido,
                    PrimeraVisita = s.PrimeraVisita,
                    Tipo = s.Tipo.Trim(),
                    Usuario = s.Usuario,
                    Vendedor = s.Vendedor
                })
                .OrderByDescending(s => s.Id);
        }

        // GET: api/SeguimientosClientes
        public IQueryable<SeguimientoClienteDTO> GetSeguimientosClientes(string vendedor, string filtro)
        {
            DateTime fechaDesde = DateTime.Today.AddYears(-3);
            var resultado = db.SeguimientosClientes
                .Include(s => s.Cliente)
                .Where(s => s.Comentarios.Contains(filtro));
                
            if (!string.IsNullOrEmpty(vendedor))
            {
                IServicioVendedores _servicioVendedores = new ServicioVendedores(); // habría que inyectarlo
                var listaVendedores = _servicioVendedores.VendedoresEquipo(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor).GetAwaiter().GetResult().Select(l => l.vendedor).ToList();
                resultado = resultado.Include(s => s.Cliente).Where(s => listaVendedores.Contains(s.Cliente.Vendedor));
            }

            return resultado.Select(s => new SeguimientoClienteDTO
            {
                Aparatos = s.Aparatos,
                Aviso = s.Aviso,
                Cliente = s.Número,
                ClienteNuevo = s.ClienteNuevo,
                Comentarios = s.Comentarios,
                Contacto = s.Contacto,
                Direccion = s.Cliente.Dirección,
                Empresa = s.Empresa,
                Estado = (SeguimientoClienteDTO.EstadoSeguimientoDTO)s.Estado,
                Fecha = s.Fecha,
                GestionAparatos = s.GestiónAparatos,
                Id = s.NºOrden,
                Nombre = s.Cliente.Nombre,
                Pedido = s.Pedido,
                PrimeraVisita = s.PrimeraVisita,
                Tipo = s.Tipo.Trim(),
                Usuario = s.Usuario,
                Vendedor = s.Vendedor
            }).OrderByDescending(s => s.Id);
        }

        [HttpGet]
        [Route("api/SeguimientosClientes/GetCodigosPostalesSinVisitar")]
        // GET: api/Clientes/5
        [ResponseType(typeof(ICollection<CodigoPostalSeguimientoLookup>))]
        public async Task<IHttpActionResult> GetCodigosPostalesSinVisitar(string vendedor, DateTime fechaDesde, DateTime fechaHasta)
        {
            GestorRapports gestor = new GestorRapports();
            ICollection<CodigoPostalSeguimientoLookup> respuesta = await gestor.CodigosPostalesSinVisitar(vendedor, fechaDesde, fechaHasta);
            return Ok(respuesta);
        }


        [HttpGet]
        [Route("api/SeguimientosClientes/GetClientesSinVisitar")]
        // GET: api/Clientes/5
        [ResponseType(typeof(ICollection<ClienteSeguimientoLookup>))]
        public async Task<IHttpActionResult> GetClientesSinVisitar(string vendedor, string codigoPostal, DateTime fechaDesde, DateTime fechaHasta)
        {
            GestorRapports gestor = new GestorRapports();
            ICollection<ClienteSeguimientoLookup> respuesta = await gestor.ClientesSinVisitar(vendedor, codigoPostal, fechaDesde, fechaHasta);
            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/SeguimientosClientes/Resumen")]
        public async Task<IHttpActionResult> GetResumenSeguimientosClientes(string empresa, string cliente, string contacto)
        {
            // Obtiene los 100 comentarios y fechas de los seguimientos más recientes
            var seguimientos = db.SeguimientosClientes
                .Where(s => s.Empresa == empresa && s.Número == cliente && s.Contacto == contacto)
                .Select(s => new { s.Fecha, s.Comentarios, s.Pedido })
                .OrderByDescending(s => s.Fecha)  // Ordenar por fecha descendente
                .Take(100)                        // Tomar solo los 100 más recientes
                .OrderBy(s => s.Fecha)            // Reordenar por fecha ascendente para mantener el orden cronológico
                .ToList();

            if (!seguimientos.Any())
            {
                return NotFound();
            }

            // Crea el texto de entrada para OpenAI, incluyendo la fecha con cada comentario
            var textoEntrada = "Resumen de seguimientos de un cliente. Los siguientes son los comentarios organizados por fecha:\n\n";
            foreach (var seguimiento in seguimientos)
            {
                var pedidoTexto = seguimiento.Pedido ? "Sí" : "No";
                textoEntrada += $"Fecha: {seguimiento.Fecha.ToString("yyyy-MM-dd HH:mm:ss")}\nComentario: {seguimiento.Comentarios}\nTerminó en pedido: {pedidoTexto}\n\n";
            }

            // Llama a OpenAI para obtener el resumen
            var resumen = await GenerarResumenOpenAIAsync(textoEntrada);

            if (string.IsNullOrEmpty(resumen))
            {
                return StatusCode(HttpStatusCode.InternalServerError);
            }

            return Ok(new { Resumen = resumen });
        }


        private async Task<string> GenerarResumenOpenAIAsync(string textoEntrada)
        {
            string apiKey = ConfigurationManager.AppSettings["OpenAIKey"];
            string endpoint = "https://api.openai.com/v1/chat/completions";

            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var payload = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new {
                            role = "system",
                            content = "Eres un asistente que analiza comentarios de clientes y extrae información comercial útil. Identifica detalles como las marcas que utiliza el cliente, los tratamientos que realiza, los productos que le interesan, y cualquier otra preferencia relevante. Organiza el resumen de forma clara para que sea útil en una interacción comercial."
                        },
                        new {
                            role = "user",
                            content = textoEntrada
                        }
                    },
                    max_tokens = 500,
                    temperature = 0.4
                };



                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    var responseJson = JsonConvert.DeserializeObject<dynamic>(responseString);
                    return responseJson?.choices[0]?.message?.content?.ToString();
                }

                return null;
            }
        }

        /*
        // GET: api/SeguimientosClientes/5
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> GetSeguimientoCliente(string id)
        {
            SeguimientoCliente seguimientoCliente = await db.SeguimientosClientes.FindAsync(id);
            if (seguimientoCliente == null)
            {
                return NotFound();
            }

            return Ok(seguimientoCliente);
        }
        */

        // PUT: api/SeguimientosClientes/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutSeguimientoCliente(SeguimientoClienteDTO seguimientoClienteDTO)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            SeguimientoCliente seguimientoCliente = db.SeguimientosClientes.SingleOrDefault(s => s.NºOrden == seguimientoClienteDTO.Id);

            if (seguimientoCliente.Usuario.Trim() != seguimientoClienteDTO.Usuario.Trim())
            {
                throw new Exception("No se pueden modificar los rapport de otro usuario");
            }

            seguimientoCliente.Aparatos = seguimientoClienteDTO.Aparatos;
            seguimientoCliente.Aviso = seguimientoClienteDTO.Aviso;
            seguimientoCliente.ClienteNuevo = seguimientoClienteDTO.ClienteNuevo;
            seguimientoCliente.Comentarios = seguimientoClienteDTO.Comentarios;
            seguimientoCliente.Contacto = seguimientoClienteDTO.Contacto;
            if (seguimientoCliente.Empresa?.Trim() != seguimientoClienteDTO.Empresa?.Trim())
            {
                seguimientoCliente.Empresa = seguimientoClienteDTO.Empresa;
            }            
            seguimientoCliente.Estado = (short)seguimientoClienteDTO.Estado;
            seguimientoCliente.Fecha = seguimientoClienteDTO.Fecha;
            seguimientoCliente.GestiónAparatos = seguimientoClienteDTO.GestionAparatos;
            seguimientoCliente.Número = seguimientoClienteDTO.Cliente;
            seguimientoCliente.Pedido = seguimientoClienteDTO.Pedido;
            seguimientoCliente.PrimeraVisita = seguimientoClienteDTO.PrimeraVisita;
            seguimientoCliente.Tipo = seguimientoClienteDTO.Tipo;
            //seguimientoCliente.Vendedor = seguimientoClienteDTO.Vendedor;


            db.Entry(seguimientoCliente).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SeguimientoClienteExists(seguimientoClienteDTO.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return StatusCode(HttpStatusCode.NoContent);
        }
        

        // POST: api/SeguimientosClientes
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> PostSeguimientoCliente(SeguimientoClienteDTO seguimientoClienteDTO)
        {
            if (!ModelState.IsValid || seguimientoClienteDTO == null)
            {
                return BadRequest(ModelState);
            }

            DateTime fechaDesde = new DateTime(seguimientoClienteDTO.Fecha.Year, seguimientoClienteDTO.Fecha.Month, seguimientoClienteDTO.Fecha.Day);
            DateTime fechaHasta = fechaDesde.AddDays(1);
            if (seguimientoClienteDTO.Estado != SeguimientoClienteDTO.EstadoSeguimientoDTO.Gestion_Administrativa && db.SeguimientosClientes.Where(s => s.Fecha >= fechaDesde && 
                s.Fecha < fechaHasta && s.Número == seguimientoClienteDTO.Cliente &&
                s.Contacto == seguimientoClienteDTO.Contacto &&
                s.Usuario.ToLower() == seguimientoClienteDTO.Usuario.ToLower()).Any())
            {
                throw new Exception(string.Format("Ya existe un seguimiento del cliente {0}/{1} para el día {2}", seguimientoClienteDTO.Cliente.Trim(), seguimientoClienteDTO.Contacto.Trim(), fechaDesde.ToShortDateString()));
            }

            string vendedorFicha = db.Clientes.SingleOrDefault(c => c.Empresa == seguimientoClienteDTO.Empresa && c.Nº_Cliente == seguimientoClienteDTO.Cliente && c.Contacto == seguimientoClienteDTO.Contacto).Vendedor?.Trim();
            string vendedorPeluqueria = db.VendedoresClientesGruposProductos.SingleOrDefault(v => v.Empresa == seguimientoClienteDTO.Empresa && v.Cliente == seguimientoClienteDTO.Cliente && v.Contacto == seguimientoClienteDTO.Contacto && v.GrupoProducto == "PEL")?.Vendedor?.Trim();
            string vendedorUsuario = ParametrosUsuarioController.LeerParametro(seguimientoClienteDTO.Empresa, seguimientoClienteDTO.Usuario, "Vendedor");

            seguimientoClienteDTO.Vendedor = vendedorUsuario;

            SeguimientoCliente seguimientoCliente = new SeguimientoCliente
            {
                Aparatos = seguimientoClienteDTO.Aparatos,
                Aviso = seguimientoClienteDTO.Aviso,
                ClienteNuevo = seguimientoClienteDTO.ClienteNuevo,
                Comentarios = seguimientoClienteDTO.Comentarios,
                Contacto = seguimientoClienteDTO.Contacto,
                Empresa = seguimientoClienteDTO.Empresa,
                Estado = (short)seguimientoClienteDTO.Estado,
                Fecha = seguimientoClienteDTO.Fecha,
                GestiónAparatos = seguimientoClienteDTO.GestionAparatos,
                Número = seguimientoClienteDTO.Cliente,
                Pedido = seguimientoClienteDTO.Pedido,
                PrimeraVisita = seguimientoClienteDTO.PrimeraVisita,
                Tipo = seguimientoClienteDTO.Tipo,
                NumOrdenExtracto = seguimientoClienteDTO.NumOrdenExtracto,
                Usuario = seguimientoClienteDTO.Usuario
            };

            if (seguimientoClienteDTO.Vendedor == vendedorFicha || seguimientoClienteDTO.Vendedor == vendedorPeluqueria)
            {
                seguimientoCliente.Vendedor = seguimientoClienteDTO.Vendedor;
            } else
            {
                seguimientoCliente.Vendedor = null;
            }

            if (seguimientoClienteDTO.TipoCentro == SeguimientoClienteDTO.TiposCentro.SoloPeluqueria && vendedorFicha != vendedorPeluqueria)
            {
                // poner vendedor general en ficha
                Cliente cliente = db.Clientes.SingleOrDefault(c => c.Empresa == seguimientoClienteDTO.Empresa && c.Nº_Cliente == seguimientoClienteDTO.Cliente && c.Contacto == seguimientoClienteDTO.Contacto);
                if (cliente != null && cliente.Vendedor != null && cliente.Vendedor.Trim() != Constantes.Vendedores.VENDEDOR_GENERAL)
                {
                    cliente.Usuario = seguimientoClienteDTO.Usuario;
                    cliente.Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL;
                }
            }

            if (seguimientoClienteDTO.TipoCentro == SeguimientoClienteDTO.TiposCentro.SoloEstetica && vendedorFicha != vendedorPeluqueria)
            {
                // poner vendedor general en peluquería
                VendedorClienteGrupoProducto clienteGrupo = db.VendedoresClientesGruposProductos.SingleOrDefault(c => c.Empresa == seguimientoClienteDTO.Empresa && c.Cliente == seguimientoClienteDTO.Cliente && c.Contacto == seguimientoClienteDTO.Contacto && c.GrupoProducto == "PEL");
                if (clienteGrupo != null && clienteGrupo.Vendedor !=null && clienteGrupo.Vendedor.Trim() != Constantes.Vendedores.VENDEDOR_GENERAL)
                {
                    clienteGrupo.Usuario = seguimientoCliente.Usuario;
                    clienteGrupo.Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL;
                }
            }
                        
            db.SeguimientosClientes.Add(seguimientoCliente);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (SeguimientoClienteExists(seguimientoCliente.NºOrden))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("No se ha podido guardar el seguimiento", ex);
            }

            return CreatedAtRoute("DefaultApi", new { id = seguimientoCliente.NºOrden }, seguimientoCliente);
        }
        /*
        // DELETE: api/SeguimientosClientes/5
        [ResponseType(typeof(SeguimientoCliente))]
        public async Task<IHttpActionResult> DeleteSeguimientoCliente(int id)
        {
            SeguimientoCliente seguimientoCliente = await db.SeguimientosClientes.SingleOrDefaultAsync(s=>s.NºOrden==id);
            if (seguimientoCliente == null)
            {
                return NotFound();
            }

            db.SeguimientosClientes.Remove(seguimientoCliente);
            await db.SaveChangesAsync();

            return Ok(seguimientoCliente);
        }
        */
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool SeguimientoClienteExists(int id)
        {
            return db.SeguimientosClientes.Count(e => e.NºOrden == id) > 0;
        }
    }
}