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
using NestoAPI.Infraestructure;
using NestoAPI.Models.Clientes;
using System.Data.Entity.Validation;
using NestoAPI.Models.Facturas;
using System.Net.Http.Headers;
using System.Numerics;
using System.Web.Http.Results;
using NestoAPI.Infraestructure.Vendedores;

namespace NestoAPI.Controllers
{
    
    public class ClientesController : ApiController
    {
        private IServicioVendedores servicioVendedores { get; }
        private NVEntities db = new NVEntities();

        // Carlos 06/07/15: lo pongo para desactivar el Lazy Loading
        public ClientesController()
        {
            db.Configuration.LazyLoadingEnabled = false;
            servicioVendedores = new ServicioVendedores();
        }

        // GET: api/Clientes
        public async Task<IQueryable<ClienteDTO>> GetClientes(string empresa, string vendedor, string filtro)
        {
            if ((filtro == null) || ((filtro.Length < 4) && (!filtro.All(c => char.IsDigit(c)))))
            {
                throw new Exception("Por favor, utilice un filtro de al menos 4 caracteres");
            }
            List<string> vendedoresLista;
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                vendedoresLista = new List<string>();
            }
            else
            {
                //// Crear una instancia del controlador VendedoresController
                //VendedoresController vendedoresController = new VendedoresController();

                //// Llamar al método GetVendedores con los parámetros deseados
                //var resultado = await vendedoresController.GetVendedores(empresa, vendedor).ConfigureAwait(false);
                List<VendedorDTO> listaVendedores;
                listaVendedores = await servicioVendedores.VendedoresEquipo(empresa, vendedor).ConfigureAwait(false);
                //// Puedes procesar el resultado y devolver la respuesta adecuada
                //if (resultado is OkNegotiatedContentResult<List<VendedorDTO>>)
                //{
                //    listaVendedores = ((OkNegotiatedContentResult<List<VendedorDTO>>)resultado).Content;
                //}
                //else //(resultado is BadRequestErrorMessageResult)
                //{
                //    var mensajeError = ((BadRequestErrorMessageResult)resultado).Message;
                //    // Manejar el error de acuerdo a tus necesidades
                //    throw new Exception(mensajeError);
                //}
                vendedoresLista = listaVendedores.Select(v => v.vendedor).ToList();
                
            }
            IQueryable<Cliente> clientesVendedor = from c in db.Clientes
                                                   join v in db.VendedoresClientesGruposProductos
                                                   
                                                   //This is how you join by multiple values
                                                   on new { empresa = c.Empresa, cliente = c.Nº_Cliente, contacto = c.Contacto } equals new { empresa = v.Empresa, cliente = v.Cliente, contacto = v.Contacto }
                                                   into jointData

                                                   //This is how you actually turn the join into a left-join
                                                   from jointRecord in jointData.DefaultIfEmpty()

                                                   //where vendedor == "" || vendedor == null || (c.Empresa == empresa && (c.Vendedor == vendedor || jointRecord.Vendedor == vendedor))
                                                   where (vendedor == "" || vendedor == null || vendedoresLista.Contains(c.Vendedor) || (jointRecord != null && vendedoresLista.Contains(jointRecord.Vendedor)))
                                                   select c;

            IQueryable<Cliente> clientesTabla = db.Clientes
                .Where(c => 
                (c.Empresa == empresa && c.Estado >= 0 && 
                ( 
                    c.Nº_Cliente.Equals(filtro) ||
                    c.Nombre.Contains(filtro) ||
                    c.Dirección.Contains(filtro) ||
                    c.Teléfono.Contains(filtro) ||
                    c.CIF_NIF.Contains(filtro) ||
                    c.Población.Contains(filtro) ||
                    c.Comentarios.Contains(filtro)
                )))
                .Select(c => c);

            IQueryable<ClienteDTO> clientes = from c in clientesTabla
                                  where c.Clientes11.Any(s => clientesVendedor.Contains(s))
                                  select new ClienteDTO
                                  {
                                      albaranValorado = c.AlbaranValorado,
                                      cadena = c.Cadena.Trim(),
                                      ccc = c.CCC.Trim(),
                                      cifNif = c.CIF_NIF.Trim(),
                                      cliente = c.Nº_Cliente.Trim(),
                                      clientePrincipal = c.ClientePrincipal,
                                      codigoPostal = c.CodPostal.Trim(),
                                      comentarioPicking = c.ComentarioPicking.Trim(),
                                      comentarioRuta = c.ComentarioRuta.Trim(),
                                      comentarios = c.Comentarios,
                                      contacto = c.Contacto.Trim(),
                                      copiasAlbaran = c.NºCopiasAlbarán,
                                      copiasFactura = c.NºCopiasFactura,
                                      direccion = c.Dirección.Trim(),
                                      empresa = c.Empresa.Trim(),
                                      estado = c.Estado,
                                      grupo = c.Grupo.Trim(),
                                      iva = c.IVA.Trim(),
                                      mantenerJunto = c.MantenerJunto,
                                      noComisiona = c.NoComisiona,
                                      nombre = c.Nombre.Trim(),
                                      periodoFacturacion = c.PeriodoFacturación.Trim(),
                                      poblacion = c.Población.Trim(),
                                      provincia = c.Provincia.Trim(),
                                      ruta = c.Ruta.Trim(),
                                      servirJunto = c.ServirJunto,
                                      telefono = c.Teléfono.Trim(),
                                      vendedor = c.Vendedor.Trim(),
                                      web = c.Web.Trim()
                                  };

            return clientes.OrderByDescending(o => o.cliente.Equals(filtro));
        }

        // GET: api/Clientes
        public IQueryable<ClienteDTO> GetClientes(string empresa, string filtro)
        {
            if (filtro.Length < 4 && !filtro.All(c => char.IsDigit(c)))
            {
                throw new Exception("Por favor, utilice un filtro de al menos 4 caracteres");
            }

            List<ClienteDTO> clientes = db.Clientes
                .Where(c => (c.Empresa == empresa && c.Estado >= 0 &&
                (
                    c.Nº_Cliente.Equals(filtro) ||
                    c.Nombre.Contains(filtro) ||
                    c.Dirección.Contains(filtro) ||
                    c.Teléfono.Contains(filtro) ||
                    c.CIF_NIF.Contains(filtro) ||
                    c.Población.Contains(filtro) ||
                    c.Comentarios.Contains(filtro)
                )))
                .Select(clienteEncontrado => new ClienteDTO
                {
                    albaranValorado = clienteEncontrado.AlbaranValorado,
                    cadena = clienteEncontrado.Cadena.Trim(),
                    ccc = clienteEncontrado.CCC.Trim(),
                    cifNif = clienteEncontrado.CIF_NIF.Trim(),
                    cliente = clienteEncontrado.Nº_Cliente.Trim(),
                    clientePrincipal = clienteEncontrado.ClientePrincipal,
                    codigoPostal = clienteEncontrado.CodPostal.Trim(),
                    comentarioPicking = clienteEncontrado.ComentarioPicking.Trim(),
                    comentarioRuta = clienteEncontrado.ComentarioRuta.Trim(),
                    comentarios = clienteEncontrado.Comentarios,
                    contacto = clienteEncontrado.Contacto.Trim(),
                    copiasAlbaran = clienteEncontrado.NºCopiasAlbarán,
                    copiasFactura = clienteEncontrado.NºCopiasFactura,
                    direccion = clienteEncontrado.Dirección.Trim(),
                    empresa = clienteEncontrado.Empresa.Trim(),
                    estado = clienteEncontrado.Estado,
                    grupo = clienteEncontrado.Grupo.Trim(),
                    iva = clienteEncontrado.IVA.Trim(),
                    mantenerJunto = clienteEncontrado.MantenerJunto,
                    noComisiona = clienteEncontrado.NoComisiona,
                    nombre = clienteEncontrado.Nombre.Trim(),
                    periodoFacturacion = clienteEncontrado.PeriodoFacturación.Trim(),
                    poblacion = clienteEncontrado.Población.Trim(),
                    provincia = clienteEncontrado.Provincia.Trim(),
                    ruta = clienteEncontrado.Ruta.Trim(),
                    servirJunto = clienteEncontrado.ServirJunto,
                    telefono = clienteEncontrado.Teléfono.Trim(),
                    vendedor = clienteEncontrado.Vendedor.Trim(),
                    web = clienteEncontrado.Web.Trim()
                }).
                OrderByDescending(o => o.cliente.Equals(filtro))
                .ToList();

            return clientes.AsQueryable();
        }

        // GET: api/Clientes
        public IQueryable<ClienteDTO> GetClientes(string filtro)
        {

            if (filtro.Length<4) {
                throw new Exception("Por favor, utilice un filtro de al menos 4 caracteres");
            }

            List<ClienteDTO> clientes = db.Clientes
                .Where(c => (c.Estado >= 0 &&
                (
                    c.Nombre.Contains(filtro) ||
                    c.Dirección.Contains(filtro) ||
                    c.Teléfono.Contains(filtro) ||
                    c.CIF_NIF.Contains(filtro) ||
                    c.Población.Contains(filtro) ||
                    c.Comentarios.Contains(filtro)
                )))
                .Select(clienteEncontrado => new ClienteDTO
                {
                    albaranValorado = clienteEncontrado.AlbaranValorado,
                    cadena = clienteEncontrado.Cadena.Trim(),
                    ccc = clienteEncontrado.CCC.Trim(),
                    cifNif = clienteEncontrado.CIF_NIF.Trim(),
                    cliente = clienteEncontrado.Nº_Cliente.Trim(),
                    clientePrincipal = clienteEncontrado.ClientePrincipal,
                    codigoPostal = clienteEncontrado.CodPostal.Trim(),
                    comentarioPicking = clienteEncontrado.ComentarioPicking.Trim(),
                    comentarioRuta = clienteEncontrado.ComentarioRuta.Trim(),
                    comentarios = clienteEncontrado.Comentarios,
                    contacto = clienteEncontrado.Contacto.Trim(),
                    copiasAlbaran = clienteEncontrado.NºCopiasAlbarán,
                    copiasFactura = clienteEncontrado.NºCopiasFactura,
                    direccion = clienteEncontrado.Dirección.Trim(),
                    empresa = clienteEncontrado.Empresa.Trim(),
                    estado = clienteEncontrado.Estado,
                    grupo = clienteEncontrado.Grupo.Trim(),
                    iva = clienteEncontrado.IVA.Trim(),
                    mantenerJunto = clienteEncontrado.MantenerJunto,
                    noComisiona = clienteEncontrado.NoComisiona,
                    nombre = clienteEncontrado.Nombre.Trim(),
                    periodoFacturacion = clienteEncontrado.PeriodoFacturación.Trim(),
                    poblacion = clienteEncontrado.Población.Trim(),
                    provincia = clienteEncontrado.Provincia.Trim(),
                    ruta = clienteEncontrado.Ruta.Trim(),
                    servirJunto = clienteEncontrado.ServirJunto,
                    telefono = clienteEncontrado.Teléfono.Trim(),
                    vendedor = clienteEncontrado.Vendedor.Trim(),
                    web = clienteEncontrado.Web.Trim()
                }).ToList();

            return clientes.AsQueryable();
        }

        [HttpGet]
        [Route("api/Clientes/GetClientesProbabilidadVenta")]
        // GET: http://localhost:53364/api/Clientes/GetClientesProbabilidadVenta?vendedor=CAM&numeroClientes=10
        [ResponseType(typeof(List<ClienteProbabilidadVenta>))]
        public async Task<IHttpActionResult> GetClientesProbabilidadVenta(string vendedor, int numeroClientes = 20, string tipoInteraccion = "", string grupoSubgrupo = "")
        {
            GestorClientes gestor = new GestorClientes();
            List<ClienteProbabilidadVenta> respuesta = await gestor.BuscarClientesPorProbabilidadVenta(vendedor, numeroClientes, tipoInteraccion, grupoSubgrupo);

            return Ok(respuesta);
        }


        // GET: api/Clientes/5
        [ResponseType(typeof(ClienteDTO))]
        public async Task<IHttpActionResult> GetCliente(string empresa, string cliente, string contacto)
        {
            Cliente clienteEncontrado;

            if (contacto != null && contacto.Trim() != "")
            {
                clienteEncontrado = await (from c in db.Clientes where c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto select c).SingleOrDefaultAsync();
            } else
            {
                clienteEncontrado = await (from c in db.Clientes where c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal select c).SingleOrDefaultAsync();
            }
            
            
            if (clienteEncontrado == null)
            {
                return NotFound();
            }

            List<VendedorGrupoProductoDTO> vendedoresGrupoProducto = db.VendedoresClientesGruposProductos.Where(v => v.Empresa == clienteEncontrado.Empresa && v.Cliente == clienteEncontrado.Nº_Cliente && v.Contacto == clienteEncontrado.Contacto)
                .Select(v => new VendedorGrupoProductoDTO
                {
                    vendedor = v.Vendedor,
                    estado = v.Estado,
                    grupoProducto = v.GrupoProducto
                })
                .ToList();

            ClienteDTO clienteDTO = new ClienteDTO
            {
                albaranValorado = clienteEncontrado.AlbaranValorado,
                cadena= clienteEncontrado.Cadena,
                ccc = clienteEncontrado.CCC,
                cifNif = clienteEncontrado.CIF_NIF,
                cliente = clienteEncontrado.Nº_Cliente.Trim(),
                clientePrincipal = clienteEncontrado.ClientePrincipal,
                codigoPostal= clienteEncontrado.CodPostal,
                comentarioPicking = clienteEncontrado.ComentarioPicking,
                comentarioRuta = clienteEncontrado.ComentarioRuta,
                comentarios = clienteEncontrado.Comentarios,
                contacto = clienteEncontrado.Contacto.Trim(),
                copiasAlbaran = clienteEncontrado.NºCopiasAlbarán,
                copiasFactura = clienteEncontrado.NºCopiasFactura,
                direccion = clienteEncontrado.Dirección,
                empresa = clienteEncontrado.Empresa.Trim(),
                estado = clienteEncontrado.Estado,
                grupo = clienteEncontrado.Grupo,
                iva = clienteEncontrado.IVA,
                mantenerJunto = clienteEncontrado.MantenerJunto,
                noComisiona= clienteEncontrado.NoComisiona,
                nombre = clienteEncontrado.Nombre,
                periodoFacturacion = clienteEncontrado.PeriodoFacturación,
                poblacion = clienteEncontrado.Población,
                provincia = clienteEncontrado.Provincia,
                ruta = clienteEncontrado.Ruta,
                servirJunto = clienteEncontrado.ServirJunto,
                telefono = clienteEncontrado.Teléfono,
                vendedor = clienteEncontrado.Vendedor,
                web = clienteEncontrado.Web ,
                VendedoresGrupoProducto = vendedoresGrupoProducto
            };

            return Ok(clienteDTO);
        }

        [HttpGet]
        [Route("api/Clientes/GetClienteCrear")]
        // GET: api/Clientes/5
        [ResponseType(typeof(ClienteCrear))]
        public async Task<IHttpActionResult> GetClienteCrear(string empresa, string cliente, string contacto)
        {
            GestorClientes gestor = new GestorClientes();
            ClienteCrear respuesta = await gestor.ConstruirClienteCrear(empresa, cliente, contacto);

            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/Clientes/ComprobarNifNombre")]
        // GET: api/Clientes/5
        [ResponseType(typeof(RespuestaNifNombreCliente))]
        public async Task<IHttpActionResult> ComprobarNifNombre(string nif, string nombre)
        {
            GestorClientes gestor = new GestorClientes();
            RespuestaNifNombreCliente respuesta = await gestor.ComprobarNifNombre(nif, nombre);

            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/Clientes/ComprobarDatosGenerales")]
        // GET: api/Clientes/5
        [ResponseType(typeof(RespuestaDatosGeneralesClientes))]
        public async Task<IHttpActionResult> ComprobarDatosGenerales(string direccion, string codigoPostal, string telefono)
        {
            GestorClientes gestor = new GestorClientes();
            RespuestaDatosGeneralesClientes respuesta = await gestor.ComprobarDatosGenerales(direccion, codigoPostal, telefono);

            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/Clientes/GetClientePorEmail")]
        // GET: api/Clientes/5
        [ResponseType(typeof(ClienteTelefonoLookup))]
        public async Task<IHttpActionResult> GetClientePorEmail(string email)
        {
            GestorClientes gestor = new GestorClientes();
            ClienteTelefonoLookup respuesta = await gestor.BuscarClientePorEmail(email);

            return Ok(respuesta);
        }

        [HttpGet]
        [Route("api/Clientes/ComprobarDatosBanco")]
        // GET: api/Clientes/5
        [ResponseType(typeof(RespuestaDatosBancoCliente))]
        public IHttpActionResult ComprobarDatosBanco(string formaPago, string plazosPago, string iban)
        {
            GestorClientes gestor = new GestorClientes();
            RespuestaDatosBancoCliente respuesta = gestor.ComprobarDatosBanco(formaPago, plazosPago, iban);

            return Ok(respuesta);
        }

        [HttpPut]
        [Route("api/Clientes/ClienteComercial")]
        // PUT: api/Clientes/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutCliente(ClienteDTO cliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            Cliente clienteDB = db.Clientes.SingleOrDefault(c => c.Empresa == cliente.empresa && c.Nº_Cliente == cliente.cliente && c.Contacto == cliente.contacto);


            if (clienteDB == null || cliente == null)
            {
                return BadRequest();
            }

            // Cambiamos los vendedores
            if (clienteDB.Vendedor != null && cliente.vendedor != null && clienteDB.Vendedor.Trim() != cliente.vendedor.Trim())
            {
                clienteDB.Vendedor = cliente.vendedor;
                clienteDB.Estado = cliente.estado;
                clienteDB.Usuario = cliente.usuario;
            }
                        
            db.Entry(clienteDB).State = System.Data.Entity.EntityState.Modified;

            // Carlos 02/03/17: gestionamos el vendedor por grupo de producto
            GestorComisiones.ActualizarVendedorClienteGrupoProducto(db, clienteDB, cliente);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ClienteExists(cliente.empresa, cliente.cliente, cliente.contacto))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            } catch (Exception ex)
            {
                throw new Exception("No se ha podido actualizar el cliente", ex);
            }

            return StatusCode(HttpStatusCode.NoContent);
        }

        [HttpPut]
        [Route("api/Clientes/DejarDeVisitar")]
        // PUT: api/Clientes/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> DejarDeVisitar(ClienteCrear cliente)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            GestorClientes gestor = new GestorClientes();            

            try
            {
                List<Cliente> clientesDB = await gestor.DejarDeVisitar(db, cliente);
                foreach (var clienteDB in clientesDB)
                {
                    db.Entry(clienteDB).State = System.Data.Entity.EntityState.Modified;
                }

                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ClienteExists(cliente.Empresa, cliente.Cliente, cliente.Contacto))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return StatusCode(HttpStatusCode.NoContent);
        }

        // PUT: api/Clientes/5
        [ResponseType(typeof(Cliente))]
        public async Task<IHttpActionResult> PutCliente(ClienteCrear clienteCrear)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }                      

            //IServicioGestorClientes servicio = new ServicioGestorClientes();
            IGestorClientes gestor = new GestorClientes();
            Cliente cliente = await gestor.PrepararClienteModificar(clienteCrear, db);         
            

            try
            {
                db.Entry(cliente).State = System.Data.Entity.EntityState.Modified;
                await db.SaveChangesAsync();
                if (cliente.CCCs.Count != 0 && cliente.CCC == null)
                {
                    cliente.CCC1 = cliente.CCCs.FirstOrDefault();
                    await db.SaveChangesAsync();
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ClienteExists(cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return CreatedAtRoute("DefaultApi", new { cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto }, cliente);
        }


        // POST: api/Clientes
        [ResponseType(typeof(Cliente))]
        public async Task<IHttpActionResult> PostCliente(ClienteCrear clienteCrear)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            ContadorGlobal contador;
            if (!clienteCrear.EsContacto)
            {
                contador = db.ContadoresGlobales.SingleOrDefault();
                clienteCrear.Cliente = contador.Clientes++.ToString();
            }

            if (clienteCrear.Empresa == null)
            {
                clienteCrear.Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            }

            IGestorClientes gestor = new GestorClientes();
            Cliente cliente = await gestor.PrepararClienteCrear(clienteCrear, db);
            db.Clientes.Add(cliente);

            try
            {
                await db.SaveChangesAsync();
                if (cliente.CCCs.Count != 0 && cliente.CCC == null)
                {
                    cliente.CCC1 = cliente.CCCs.FirstOrDefault();
                    await db.SaveChangesAsync();
                }
            }
            catch (DbUpdateException ex)
            {
                if (ClienteExists(cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto))
                {
                    return Conflict();
                }
                else
                {
                    throw ex;
                }
            }
            catch (DbEntityValidationException ex)
            {
                if (ex.EntityValidationErrors.Any())
                {
                    throw new Exception(ex.EntityValidationErrors.First().ValidationErrors.First().ErrorMessage);
                }
                else
                {
                    throw ex;
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
            }

            return CreatedAtRoute("DefaultApi", new { cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto }, cliente);
        }

        //// POST: api/Clientes
        //[ResponseType(typeof(Cliente))]
        //public async Task<IHttpActionResult> PostCliente(Cliente cliente)
        //{
        //    if (!ModelState.IsValid)
        //    {
        //        return BadRequest(ModelState);
        //    }

        //    db.Clientes.Add(cliente);

        //    try
        //    {
        //        await db.SaveChangesAsync();
        //    }
        //    catch (DbUpdateException)
        //    {
        //        if (ClienteExists(cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto))
        //        {
        //            return Conflict();
        //        }
        //        else
        //        {
        //            throw;
        //        }
        //    }

        //    return CreatedAtRoute("DefaultApi", new { cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto }, cliente);
        //}

        //// DELETE: api/Clientes/5
        //[ResponseType(typeof(Cliente))]
        //public async Task<IHttpActionResult> DeleteCliente(string id)
        //{
        //    Cliente cliente = await db.Clientes.FindAsync(id);
        //    if (cliente == null)
        //    {
        //        return NotFound();
        //    }

        //    db.Clientes.Remove(cliente);
        //    await db.SaveChangesAsync();

        //    return Ok(cliente);
        //}

        [HttpGet]
        [Route("api/Clientes/MandatoPDF")]
        //http://localhost:53364/api/Clientes/MandatoPDF?empresa=1&cliente=1&contacto=1&ccc=1
        public async Task<HttpResponseMessage> GetMandato(string empresa, string cliente, string contacto, string ccc)
        {
            GestorClientes gestor = new GestorClientes();
            Mandato mandato = await gestor.LeerMandato(empresa, cliente, contacto, ccc).ConfigureAwait(true);
            if (!mandato.Iban.EsValido)
            {
                throw new Exception($"El IBAN {mandato.Iban.Formateado} no es válido");
            }
            List<Mandato> mandatos = new List<Mandato> { mandato };

            var result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = gestor.MandatoEnPDF(mandatos)
            };
            //result.Content.Headers.ContentDisposition =
            //    new ContentDispositionHeaderValue("attachment")
            //    {
            //        FileName = factura.Item2 + ".pdf"
            //    };
            result.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/pdf");

            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }


        private bool ClienteExists(string empresa, string numCliente, string contacto)
        {
            return db.Clientes.Count(e => e.Empresa == empresa && e.Nº_Cliente == numCliente && e.Contacto ==contacto) > 0;
        }
    }
}