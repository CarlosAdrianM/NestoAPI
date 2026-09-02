using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace NestoAPI.Infraestructure
{
    internal class ServicioGestorClientes : IServicioGestorClientes
    {
        public async Task<ClienteDTO> BuscarClientePorNif(string nif)
        {
            using (NVEntities db = new NVEntities())
            {
                try
                {
                    string nifSinCero = nif.TrimStart('0');
                    Cliente cliente = await db.Clientes.FirstOrDefaultAsync(
                    c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.ClientePrincipal == true && c.CIF_NIF != null &&
                    (c.CIF_NIF == nif || c.CIF_NIF == nifSinCero)
                ).ConfigureAwait(false);
                    if (cliente != null)
                    {
                        ClienteDTO clienteDTO = new ClienteDTO
                        {
                            empresa = cliente.Empresa?.Trim(),
                            cliente = cliente.Nº_Cliente?.Trim(),
                            contacto = cliente.Contacto?.Trim(),
                            cifNif = cliente.CIF_NIF?.Trim(),
                            nombre = cliente.Nombre?.Trim()
                        };
                        return clienteDTO;
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        // NestoAPI#388 (guarda 20/08/26): un solo aviso diario en ELMAH mientras dure el modo
        // degradado — sin él, cada validación metía su propio error y ensuciaba el log.
        private static DateTime _fechaUltimoAvisoDegradado = DateTime.MinValue;

        public async Task<RespuestaNifNombreCliente> ComprobarNifNombre(string nif, string nombre)
        {
            byte[] bytesNif = Encoding.Default.GetBytes(nif?.ToUpper().Trim());
            nif = Encoding.UTF8.GetString(bytesNif);

            byte[] bytesNombre = Encoding.Default.GetBytes(nombre?.ToUpper().Trim());
            nombre = Encoding.UTF8.GetString(bytesNombre);

            HttpWebRequest request;
            try
            {
                request = CreateWebRequest();
            }
            catch (Exceptions.NestoBusinessException ex)
            {
                // GUARDA #388 (20/08/26): sin certificado de la AEAT VIGENTE (caducó y el
                // renovado aún no está importado) NO se rompe ningún flujo. Menos es nada
                // (petición de Carlos): se valida en LOCAL el formato del NIF/CIF con su
                // dígito de control (TieneFormatoNif, el algoritmo clásico) — un NIF bien
                // formado pasa como bueno SIN VERIFICAR contra el censo (nada se cachea como
                // validado); uno mal formado se rechaza igual que lo rechazaría la AEAT. En
                // cuanto se importe el certificado renovado en el almacén
                // (RenovarCertificadoAeat.ps1), ObtenerCertificado lo encuentra solo y la
                // validación real vuelve SIN recompilar ni redesplegar.
                if (_fechaUltimoAvisoDegradado.Date != DateTime.Today)
                {
                    _fechaUltimoAvisoDegradado = DateTime.Now;
                    ElmahHelper.Log(new Exception(
                        "CertificadoAeat: MODO DEGRADADO — la validación de NIF contra el censo está " +
                        "desactivada (certificado caducado); los NIF con formato válido (algoritmo " +
                        "local) se están dando por buenos sin verificar. Importar el renovado cuanto " +
                        $"antes. Detalle: {ex.Message}"));
                }
                bool formatoValido = Clientes.ServicioValidacionNif.TieneFormatoNif(nif);
                // GUARDA #388 (21/08/26): si el NIF es de persona JURÍDICA, el cliente no deja
                // escribir el nombre y manda un relleno ("UNDEFINED"/"undefined") esperando que
                // le devolvamos la razón social del censo. Sin censo NO hay razón social que
                // devolver: se devuelve el nombre VACÍO (nunca el relleno, que acabaría siendo
                // el nombre del cliente) y SinVerificar=true, para que el cliente habilite el
                // campo y lo escriba el usuario. Los clientes que aún no entienden SinVerificar
                // se topan con la guarda de PrepararClienteCrear al grabar.
                bool nombreLoPoniaElCenso = Clientes.NombreFiscalPlaceholder.EsRelleno(nombre);
                return new RespuestaNifNombreCliente
                {
                    NifValidado = formatoValido,
                    SinVerificar = true,
                    NifFormateado = nif,
                    NombreFormateado = nombreLoPoniaElCenso ? string.Empty : nombre,
                    NombreLoDebeEscribirElUsuario = nombreLoPoniaElCenso,
                    ResultadoAeat = formatoValido
                        ? "SIN VERIFICAR (certificado AEAT caducado): formato de NIF válido"
                        : "SIN VERIFICAR (certificado AEAT caducado): el NIF NO tiene un formato válido"
                };
            }
            XmlDocument soapEnvelopeXml = new XmlDocument();
            soapEnvelopeXml.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
                <soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:vnif=""http://www2.agenciatributaria.gob.es/static_files/common/internet/dep/aplicaciones/es/aeat/burt/jdit/ws/VNifV2Ent.xsd"">
                <soapenv:Header/>
                <soapenv:Body>
                    <vnif:VNifV2Ent>
                        <vnif:Contribuyente>
                            <vnif:Nif> " + System.Security.SecurityElement.Escape(nif) + @" </vnif:Nif>
                            <vnif:Nombre> " + System.Security.SecurityElement.Escape(nombre) + @" </vnif:Nombre>
                        </vnif:Contribuyente>
                    </vnif:VNifV2Ent>
                </soapenv:Body>
                </soapenv:Envelope>

            ");

            using (Stream stream = request.GetRequestStream())
            {
                soapEnvelopeXml.Save(stream);
            }

            string nifDevuelto;
            string nombreDevuelto;
            string resultadoDevuelto;
            //try
            //{
            //    WebResponse response = await request.GetResponseAsync();
            //}
            //catch (Exception ex)
            //{
            //    throw ex;
            //}
            using (WebResponse response = await request.GetResponseAsync())
            {
                using (StreamReader rd = new StreamReader(response.GetResponseStream()))
                {
                    string soapResult = rd.ReadToEnd();
                    XmlDocument xml = new XmlDocument();
                    xml.LoadXml(soapResult);
                    XmlNamespaceManager nsmgr = new XmlNamespaceManager(xml.NameTable);
                    nsmgr.AddNamespace("VNifV2Sal", "http://www2.agenciatributaria.gob.es/static_files/common/internet/dep/aplicaciones/es/aeat/burt/jdit/ws/VNifV2Sal.xsd");
                    XmlNode contribuyente = xml.DocumentElement.FirstChild.FirstChild.FirstChild;
                    nifDevuelto = contribuyente.SelectSingleNode("VNifV2Sal:Nif", nsmgr).InnerText;
                    nombreDevuelto = contribuyente.SelectSingleNode("VNifV2Sal:Nombre", nsmgr).InnerText;
                    resultadoDevuelto = contribuyente.SelectSingleNode("VNifV2Sal:Resultado", nsmgr).InnerText;
                }
            }

            return ConstruirRespuestaCifNombre(nifDevuelto, nombreDevuelto, resultadoDevuelto);
        }

        // NestoAPI#166: extraído para cubrir con tests la interpretación del resultado
        // que devuelve la AEAT. Códigos manejados:
        //   IDENTIFICADO               → NIF válido
        //   NO IDENTIFICADO-SIMILAR    → NIF válido (datos coinciden aproximadamente)
        //   IDENTIFICADO-BAJA          → NIF válido, empresa dada de baja (prefijo aviso)
        //   IDENTIFICADO-REVOCADO      → NIF válido, NIF revocado por AEAT (prefijo aviso)
        internal static RespuestaNifNombreCliente ConstruirRespuestaCifNombre(
            string nifDevuelto, string nombreDevuelto, string resultadoDevuelto)
        {
            string resultadoUpper = resultadoDevuelto?.ToUpper();

            if (resultadoUpper == "IDENTIFICADO-BAJA")
            {
                nombreDevuelto = "¡EMPRESA DE BAJA! " + nombreDevuelto;
            }
            else if (resultadoUpper == "IDENTIFICADO-REVOCADO")
            {
                // NIF revocado por Hacienda: el cliente debe pedir rehabilitación.
                nombreDevuelto = "¡NIF REVOCADO! " + nombreDevuelto;
            }

            if (nombreDevuelto != null && nombreDevuelto.Length > 50)
            {
                nombreDevuelto = nombreDevuelto.Substring(0, 50);
            }

            return new RespuestaNifNombreCliente
            {
                NifFormateado = nifDevuelto?.Trim(),
                NombreFormateado = nombreDevuelto?.Trim(),
                ResultadoAeat = resultadoDevuelto?.Trim(),
                NifValidado = resultadoUpper == "IDENTIFICADO" ||
                              resultadoUpper == "NO IDENTIFICADO-SIMILAR" ||
                              resultadoUpper == "IDENTIFICADO-BAJA" ||
                              resultadoUpper == "IDENTIFICADO-REVOCADO"
            };
        }

        public async Task<RespuestaDatosGeneralesClientes> CogerDatosCodigoPostal(string codigoPostal)
        {
            using (NVEntities db = new NVEntities())
            {
                CodigoPostal cp = await db.CodigosPostales.SingleOrDefaultAsync(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.Número == codigoPostal);
                if (cp == null)
                {
                    // TO DO: leerlo de algún webservice de correos y crearlo
                    throw new ArgumentException("No existe el código postal " + codigoPostal + " en la base de datos");
                }

                RespuestaDatosGeneralesClientes respuesta = new RespuestaDatosGeneralesClientes
                {
                    CodigoPostal = codigoPostal,
                    Poblacion = cp.Descripción?.Trim(),
                    Provincia = cp.Provincia?.Trim(),
                    Ruta = cp.Ruta,
                    VendedorEstetica = cp.Vendedor
                };

                VendedorCodigoPostalGrupoProducto vendedorPeluqueria = await db.VendedoresCodigoPostalGruposProductos
                    .SingleOrDefaultAsync(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA && v.CodigoPostal == codigoPostal);

                if (vendedorPeluqueria != null)
                {
                    respuesta.VendedorPeluqueria = vendedorPeluqueria.Vendedor?.Trim();
                }

                return respuesta;
            }

        }

        private static HttpWebRequest CreateWebRequest()
        {
            string host = @"https://www1.agenciatributaria.gob.es/wlpl/BURT-JDIT/ws/VNifV2SOAP";

            // NestoAPI#388: OJO, aquí no puede haber ServerCertificateValidationCallback => true:
            // ServicePointManager es global al proceso y anularía la validación TLS de TODAS las
            // llamadas salientes (Verifacti, Redsys, Amazon...), no solo la de la AEAT.
            X509Certificate2 certificado = Clientes.ProveedorCertificadoAeat.ObtenerCertificado();


            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(host);
            webRequest.AllowAutoRedirect = true;
            webRequest.ClientCertificates = new X509Certificate2Collection(certificado);

            webRequest.Headers.Add(@"SOAP:Action");
            webRequest.ContentType = "text/xml;charset=\"utf-8\"";
            webRequest.Accept = "text/xml";
            webRequest.Method = "POST";
            return webRequest;
        }

        public async Task<string> CalcularSiguienteContacto(string empresa, string cliente)
        {
            NVEntities db = new NVEntities();
            bool existe = true;
            int contador = -1;
            const int MAXIMO_NUMERO_CONTACTOS = 100;
            while (existe && contador < MAXIMO_NUMERO_CONTACTOS)
            {
                contador++;
                existe = await db.Clientes.SingleOrDefaultAsync(e => e.Empresa == empresa && e.Nº_Cliente == cliente && e.Contacto == contador.ToString()) != null;
            }

            return contador.ToString();
        }

        public Task<CCC> PrepararCCC(ClienteCrear clienteCrear)
        {
            throw new NotImplementedException();
        }

        public async Task<Cliente> BuscarCliente(string empresa, string cliente, string contacto)
        {
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            return await BuscarCliente(db, empresa, cliente, contacto);
        }

        public async Task<Cliente> BuscarCliente(NVEntities db, string empresa, string cliente, string contacto)
        {
            // Issue #283: SingleOrDefault (no Single): si el cliente/contacto no existe se
            // devuelve null y cada llamante decide (los flujos de usuario lanzan NotFoundException
            // con mensaje claro en vez del 'Sequence contains no elements' de SingleAsync).
            Cliente clienteDevolver = await db.Clientes.Include(c => c.CondPagoClientes)
                .Include(c => c.CCC1).Include(c => c.Vendedore).Include(c => c.PersonasContactoClientes)
                .Include(c => c.VendedoresClienteGrupoProductoes)
                .SingleOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto);

            return clienteDevolver;
        }

        public async Task<VendedorClienteGrupoProducto> BuscarVendedorGrupo(string empresa, string cliente, string contacto, string grupo)
        {
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;

            VendedorClienteGrupoProducto vendedorGrupo = await db.VendedoresClientesGruposProductos.SingleOrDefaultAsync(v => v.Empresa == empresa && v.Cliente == cliente && v.Contacto == contacto && v.GrupoProducto == grupo);

            return vendedorGrupo;
        }

        public async Task<CondPagoCliente> BuscarCondicionesPago(string empresa, string cliente, string contacto)
        {
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;

            CondPagoCliente condPagoCliente = await db.CondPagoClientes.OrderBy(c => c.ImporteMínimo).FirstOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto);

            return condPagoCliente;
        }

        public async Task<CCC> BuscarCCC(string empresa, string cliente, string contacto, string ccc)
        {
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;

            CCC cccCliente = await db.CCCs.SingleOrDefaultAsync(c => c.Empresa == empresa && c.Cliente == cliente && c.Contacto == contacto && c.Número == ccc);

            return cccCliente;

        }

        public async Task<List<PersonaContactoCliente>> BuscarPersonasContacto(string empresa, string cliente, string contacto)
        {
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;

            List<PersonaContactoCliente> personas = await db.PersonasContactoClientes.Where(c => c.Empresa == empresa && c.NºCliente == cliente && c.Contacto == contacto).ToListAsync();

            return personas;
        }

        // Nesto#340 (1C.8, slice 4): la tabla Cargos no está mapeada en el EDMX de NestoAPI, así
        // que se lee con SQL crudo aliasando las columnas con caracteres especiales a ASCII
        // (mismo patrón que ExtractoInmovilizado en Alquileres). Es un catálogo pequeño: se trae
        // entero y se filtra en memoria.
        public async Task<Dictionary<short, string>> LeerDescripcionesCargos()
        {
            using (NVEntities db = new NVEntities())
            {
                List<FilaCargo> cargos = await db.Database
                    .SqlQuery<FilaCargo>("SELECT [NºOrden] AS Numero, [Descripción] AS Descripcion FROM Cargos")
                    .ToListAsync()
                    .ConfigureAwait(false);
                return cargos.ToDictionary(c => c.Numero, c => c.Descripcion?.Trim());
            }
        }

        private class FilaCargo
        {
            public short Numero { get; set; }
            public string Descripcion { get; set; }
        }

        // 1C.8 slice 5: la tabla EstadosCCC no está mapeada en el EDMX de NestoAPI,
        // así que se lee con SQL crudo aliasando los acentos (mismo patrón que Cargos)
        public async Task<List<EstadoCCCDTO>> LeerEstadosCCC(string empresa)
        {
            using (NVEntities db = new NVEntities())
            {
                List<FilaEstadoCCC> estados = await db.Database
                    .SqlQuery<FilaEstadoCCC>(
                        "SELECT [Número] AS Numero, [Descripción] AS Descripcion FROM EstadosCCC WHERE Empresa = @empresa ORDER BY [Número]",
                        new System.Data.SqlClient.SqlParameter("@empresa", empresa))
                    .ToListAsync()
                    .ConfigureAwait(false);
                return estados.Select(e => new EstadoCCCDTO
                {
                    numero = e.Numero,
                    descripcion = e.Descripcion?.Trim()
                }).ToList();
            }
        }

        private class FilaEstadoCCC
        {
            public short Numero { get; set; }
            public string Descripcion { get; set; }
        }

        public async Task<List<ClienteTelefonoLookup>> ClientesMismoTelefono(string telefono)
        {
            if (telefono.Length < 7)
            {
                return new List<ClienteTelefonoLookup>();
            }
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;

            List<ClienteTelefonoLookup> clientes = await db.Clientes.Where(c => c.Teléfono.Contains(telefono)).Take(5).Select(c => new ClienteTelefonoLookup
            {
                Empresa = c.Empresa.Trim(),
                Cliente = c.Nº_Cliente.Trim(),
                Contacto = c.Contacto.Trim(),
                Nombre = c.Nombre != null ? c.Nombre.Trim() : ""
            }).ToListAsync();
            List<ClienteTelefonoLookup> personas = await db.PersonasContactoClientes.Where(c => c.Teléfono.Contains(telefono)).Take(5).Select(c => new ClienteTelefonoLookup
            {
                Empresa = c.Empresa.Trim(),
                Cliente = c.NºCliente.Trim(),
                Contacto = c.Contacto.Trim(),
                Nombre = c.Nombre != null ? c.Nombre.Trim() : ""
            }).ToListAsync();

            clientes.AddRange(personas);
            List<ClienteTelefonoLookup> todos = clientes.Distinct().ToList();

            return todos;
        }

        public async Task<List<string>> VendedoresQueRecibenClientes()
        {
            using (NVEntities db = new NVEntities())
            {
                return await db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && (v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO || v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_MINI) && v.TipoComisión == "7")
                    .Select(v => v.Número)
                    .ToListAsync();
            }
        }
        public async Task<List<string>> VendedoresTelefonicos()
        {
            using (NVEntities db = new NVEntities())
            {
                return await db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO)
                    .Select(v => v.Número)
                    .ToListAsync();
            }
        }

        public DateTime Hoy()
        {
            return DateTime.Now;
        }

        public async Task<List<string>> VendedoresPresenciales()
        {
            using (NVEntities db = new NVEntities())
            {
                return await db.Vendedores.Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_PRESENCIAL)
                    .Select(v => v.Número)
                    .ToListAsync();
            }
        }

        //public async Task<List<string>> VendedoresContactosCliente(string empresa, string cliente, string contacto)
        //{
        //    using (NVEntities db = new NVEntities())
        //    {
        //        var contactos = db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto != contacto).Select(c => c.Vendedor);
        //        var vendedoresGrupo = db.VendedoresClientesGruposProductos.Where(c => c.Empresa == empresa && c.Cliente == cliente && c.Contacto != contacto).Select(c => c.Vendedor);
        //        var todosVendedores = contactos.Union(vendedoresGrupo).Distinct();
        //        return await todosVendedores.ToListAsync();
        //    }
        //}

        public async Task<List<Cliente>> BuscarContactos(string empresa, string cliente, string contacto)
        {
            using (NVEntities db = new NVEntities())
            {
                return await BuscarContactos(db, empresa, cliente, contacto);
            }
        }
        public async Task<List<Cliente>> BuscarContactos(NVEntities db, string empresa, string cliente, string contacto)
        {
            return await db.Clientes.Include(v => v.VendedoresClienteGrupoProductoes).Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto != contacto &&
                c.Estado >= Constantes.Clientes.Estados.VISITA_PRESENCIAL).ToListAsync();
        }

        public async Task<ClienteTelefonoLookup> BuscarClientePorEmail(string email)
        {
            using (NVEntities db = new NVEntities())
            {
                // Caso perfecto en el que existe y solo hay uno
                PersonaContactoCliente personaContactoCliente = await db.PersonasContactoClientes.FirstAsync(p => p.CorreoElectrónico == email).ConfigureAwait(false);
                return new ClienteTelefonoLookup
                {
                    Empresa = personaContactoCliente.Empresa.Trim(),
                    Cliente = personaContactoCliente.NºCliente.Trim(),
                    Contacto = personaContactoCliente.Contacto.Trim(),
                    Nombre = personaContactoCliente.Nombre.Trim()
                };
            }
        }

        public async Task<SeguimientoCliente> BuscarSeguimiento(string empresa, string cliente, string contacto)
        {
            using (NVEntities db = new NVEntities())
            {
                SeguimientoCliente seguimiento = await db.SeguimientosClientes.Where(s => s.Empresa == empresa && s.Número == cliente && s.Contacto == contacto).OrderByDescending(s => s.NºOrden).FirstOrDefaultAsync().ConfigureAwait(false);
                return seguimiento;
            }
        }

        public async Task<CCC> BuscarIban(NVEntities db, string empresa, string cliente, Iban iban)
        {
            CCC ibanEncontrado = await db.CCCs.Where(c => c.Empresa == empresa && c.Cliente == cliente && c.Entidad == iban.Entidad && c.Oficina == iban.Oficina && c.Nº_Cuenta == iban.NumeroCuenta).OrderByDescending(c => c.Fecha_Modificación).FirstOrDefaultAsync().ConfigureAwait(false);
            return ibanEncontrado;
        }

        public async Task<CCC> BuscarIban(NVEntities db, string empresa, string cliente, string contacto, Iban iban)
        {
            CCC ibanEncontrado = await db.CCCs.Where(c => c.Empresa == empresa && c.Cliente == cliente && c.Contacto == contacto && c.Entidad == iban.Entidad && c.Oficina == iban.Oficina && c.Nº_Cuenta == iban.NumeroCuenta).OrderByDescending(c => c.Fecha_Modificación).FirstOrDefaultAsync().ConfigureAwait(false);
            return ibanEncontrado;
        }

        public async Task<int> MayorCCC(string empresa, string cliente)
        {
            using (NVEntities db = new NVEntities())
            {
                string mayorCCC = await db.CCCs.Where(c => c.Empresa == empresa && c.Cliente == cliente).OrderByDescending(c => c.Número).Select(c => c.Número).FirstOrDefaultAsync().ConfigureAwait(false);
                return int.TryParse(mayorCCC, out int mayor) ? mayor : 0;
            }
        }

        public async Task<bool> CrearCCC(CCC nuevoCCC)
        {
            using (NVEntities db = new NVEntities())
            {
                _ = db.CCCs.Add(nuevoCCC);
                int grabado = await db.SaveChangesAsync().ConfigureAwait(false);
                return grabado > 0;
            }
        }

        public async Task<bool> RecuperarCCC(CCC cccEncontrado)
        {
            using (NVEntities db = new NVEntities())
            {
                CCC cccRecuperar = await db.CCCs.SingleAsync(c => c.Empresa == cccEncontrado.Empresa && c.Cliente == cccEncontrado.Cliente && c.Contacto == cccEncontrado.Contacto && c.Número == cccEncontrado.Número).ConfigureAwait(false);
                cccRecuperar.Estado = 0; // sin mandato
                int modificado = await db.SaveChangesAsync().ConfigureAwait(false);
                return modificado > 0;
            }
        }

        public async Task<CodigoPostal> BuscarCodigoPostal(string empresa, string codigoPostal)
        {
            using (NVEntities db = new NVEntities())
            {
                CodigoPostal cpDB = await db.CodigosPostales.SingleOrDefaultAsync(c => c.Empresa == empresa && c.Número == codigoPostal).ConfigureAwait(false);
                return cpDB;
            }
        }

        // Nesto#340: búsqueda de clientes activos por teléfono para los pedidos de canales
        // externos (antes CanalExternoPedidosAmazon consultaba la BD con EF desde el cliente)
        public async Task<List<ClienteDTO>> BuscarClientesPorTelefono(string telefono)
        {
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                return await db.Clientes
                    .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.Estado >= 0 && c.Teléfono.Contains(telefono))
                    .OrderBy(c => c.Nº_Cliente).ThenBy(c => c.Contacto)
                    .Take(5)
                    .Select(c => new ClienteDTO
                    {
                        empresa = c.Empresa.Trim(),
                        cliente = c.Nº_Cliente.Trim(),
                        contacto = c.Contacto.Trim(),
                        contactoCobro = c.ContactoCobro.Trim(),
                        clientePrincipal = c.ClientePrincipal,
                        nombre = c.Nombre.Trim(),
                        direccion = c.Dirección.Trim(),
                        codigoPostal = c.CodPostal.Trim(),
                        poblacion = c.Población.Trim(),
                        provincia = c.Provincia.Trim(),
                        telefono = c.Teléfono.Trim(),
                        vendedor = c.Vendedor.Trim(),
                        iva = c.IVA.Trim(),
                        comentarioPicking = c.ComentarioPicking,
                        estado = c.Estado
                    })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        // Nesto#340: búsqueda de cliente por NIF para los pedidos de la tienda online
        // (antes CanalExternoPedidosPrestashopNuevaVision consultaba la BD con EF desde el
        // cliente de escritorio). Semántica calcada del cliente viejo: primero coincidencia
        // EXACTA y, si no hay, Contains; solo clientes principales activos.
        public async Task<List<ClienteDTO>> BuscarClientesPorNif(string nif)
        {
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                List<ClienteDTO> exactos = await ConsultaClientesPorNif(db, c => c.CIF_NIF == nif);
                return exactos.Any()
                    ? exactos
                    : await ConsultaClientesPorNif(db, c => c.CIF_NIF.Contains(nif));
            }
        }

        private static async Task<List<ClienteDTO>> ConsultaClientesPorNif(NVEntities db,
            System.Linq.Expressions.Expression<Func<Cliente, bool>> filtroNif)
        {
            return await db.Clientes
                .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.ClientePrincipal && c.Estado >= 0)
                .Where(filtroNif)
                .OrderBy(c => c.Nº_Cliente).ThenBy(c => c.Contacto)
                .Take(5)
                .Select(c => new ClienteDTO
                {
                    empresa = c.Empresa.Trim(),
                    cliente = c.Nº_Cliente.Trim(),
                    contacto = c.Contacto.Trim(),
                    contactoCobro = c.ContactoCobro.Trim(),
                    contactoDefecto = c.ContactoDefecto.Trim(),
                    clientePrincipal = c.ClientePrincipal,
                    nombre = c.Nombre.Trim(),
                    direccion = c.Dirección.Trim(),
                    codigoPostal = c.CodPostal.Trim(),
                    poblacion = c.Población.Trim(),
                    provincia = c.Provincia.Trim(),
                    telefono = c.Teléfono.Trim(),
                    cifNif = c.CIF_NIF.Trim(),
                    vendedor = c.Vendedor.Trim(),
                    iva = c.IVA.Trim(),
                    comentarioPicking = c.ComentarioPicking,
                    estado = c.Estado
                })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<ClienteDTO> BuscarClientePorEmailNif(string email, string nif)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(nif))
            {
                return new ClienteDTO();
            }

            using (NVEntities db = new NVEntities())
            {
                string nifNormalizado = NormalizarNif(nif);
                // NestoAPI#425: el NIF se normalizaba con esmero y al email no se le hacía ni un
                // Trim. Un espacio DELANTE (pegar desde otra app, teclado del móvil) rompía la
                // búsqueda y el cliente veía "cliente no encontrado" con su correo bien escrito.
                // (Los espacios finales y las mayúsculas ya los perdonaba SQL por la semántica de
                // char y la collation; el Trim cubre el hueco que quedaba.)
                string emailNormalizado = email.Trim();

                // El filtro por NIF va en memoria a la fuerza: NormalizarNif no se puede traducir
                // a SQL. Por eso primero se acotan por email (eso sí lo hace SQL) y luego se elige.
                List<Cliente> candidatos = await db.Clientes
                    .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                                c.PersonasContactoClientes.Any(p => p.CorreoElectrónico == emailNormalizado))
                    .Include(c => c.PersonasContactoClientes)
                    .ToListAsync().ConfigureAwait(false);

                Cliente cliente = ElegirFichaParaLogin(candidatos, nifNormalizado);

                return MapearClienteParaLogin(cliente, email);
            }
        }

        /// <summary>
        /// NestoAPI#429 (punto 3): con varias fichas del mismo email+NIF, antes decidía SQL Server
        /// (FirstOrDefault sin OrderBy). Ahora gana la principal, y a igualdad, el contacto menor.
        /// </summary>
        internal static Cliente ElegirFichaParaLogin(IEnumerable<Cliente> candidatos, string nifNormalizado)
        {
            return candidatos
                .Where(c => NormalizarNif(c.CIF_NIF) == nifNormalizado)
                .OrderByDescending(c => c.ClientePrincipal)
                .ThenBy(c => c.Contacto?.Trim())
                .FirstOrDefault();
        }

        /// <summary>
        /// NestoAPI#428 (punto 5): lo que se sirve al login anónimo de la tienda, y nada más.
        ///
        /// <para>Este DTO sale por un endpoint SIN autenticación a quien acierte el par email+NIF,
        /// así que cada campo tiene que ganarse el puesto: se sirven los que la app bindea
        /// (ClienteModel de TiendasNuevaVision) y se quitan los demás. En particular, de las
        /// personas de contacto solo viaja LA DEL EMAIL CONSULTADO —el llamante ya lo conoce, lo
        /// único nuevo es el flag de facturación electrónica, que la app necesita para
        /// PermitirVerFacturas—. Antes viajaban nombre y correo de TODAS las personas de contacto
        /// del cliente: datos personales de terceros. El vendedor (nombre de un empleado) también
        /// viajaba y la app no lo usa.</para>
        ///
        /// <para>Null-safe a conciencia (#429 punto 3): Nombre, Dirección o el resto de campos de
        /// la ficha pueden ser null con datos legítimos pero incompletos, y aquí antes reventaba
        /// con NullReferenceException.</para>
        /// </summary>
        internal static ClienteDTO MapearClienteParaLogin(Cliente cliente, string email)
        {
            if (cliente == null)
            {
                return new ClienteDTO();
            }

            string emailNormalizado = email?.Trim();

            return new ClienteDTO
            {
                cliente = cliente.Nº_Cliente?.Trim(),
                contacto = cliente.Contacto?.Trim(),
                cifNif = cliente.CIF_NIF?.Trim(),
                nombre = cliente.Nombre?.Trim(),
                direccion = cliente.Dirección?.Trim(),
                telefono = cliente.Teléfono?.Trim(),
                poblacion = cliente.Población?.Trim(),
                codigoPostal = cliente.CodPostal?.Trim(),
                estado = cliente.Estado,
                provincia = cliente.Provincia?.Trim(),
                PersonasContacto = (cliente.PersonasContactoClientes ?? Enumerable.Empty<PersonaContactoCliente>())
                    .Where(p => string.Equals(p.CorreoElectrónico?.Trim(), emailNormalizado, StringComparison.OrdinalIgnoreCase))
                    .Select(p => new PersonaContactoDTO
                    {
                        CorreoElectronico = p.CorreoElectrónico?.Trim(),
                        FacturacionElectronica = p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO,
                        PedidosSinPrecios = p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_PEDIDOS_SIN_PRECIOS
                    }).ToList()
            };
        }

        /// <summary>
        /// NestoAPI#438: copia las personas de contacto y los CCC del contacto PRINCIPAL a otro
        /// contacto del mismo cliente. Es lo que hoy hace administración a mano cada vez que un
        /// vendedor crea un contacto y pide por correo "copiad el email de las facturas y los
        /// datos de banco del principal": la pregunta se la hacen Nesto y NestoApp al crear el
        /// contacto, y la copia vive aquí una sola vez.
        /// </summary>
        public async Task<ResultadoCopiaDatosPrincipal> CopiarDatosDelPrincipal(
            string empresa, string cliente, string contactoDestino, string usuario)
        {
            if (string.IsNullOrWhiteSpace(empresa))
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            }

            using (NVEntities db = new NVEntities())
            {
                List<Cliente> fichas = await db.Clientes
                    .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente)
                    .Include(c => c.PersonasContactoClientes)
                    .Include(c => c.CCCs)
                    .ToListAsync().ConfigureAwait(false);

                Cliente principal = fichas.FirstOrDefault(c => c.ClientePrincipal);
                Cliente destino = fichas.FirstOrDefault(c =>
                    string.Equals(c.Contacto?.Trim(), contactoDestino?.Trim(), StringComparison.OrdinalIgnoreCase));

                ResultadoCopiaDatosPrincipal resultado = PrepararCopiaDelPrincipal(principal, destino, usuario, DateTime.Now);
                if (resultado.Error != null)
                {
                    return resultado;
                }

                foreach (PersonaContactoCliente persona in resultado.NuevasPersonas)
                {
                    _ = db.PersonasContactoClientes.Add(persona);
                }
                foreach (CCC ccc in resultado.NuevosCccs)
                {
                    _ = db.CCCs.Add(ccc);
                }
                if (resultado.CccAsignado != null)
                {
                    destino.CCC = resultado.CccAsignado;
                    destino.Usuario = usuario;
                    destino.Fecha_Modificación = DateTime.Now;
                }

                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return resultado;
            }
        }

        /// <summary>
        /// El núcleo puro de la copia (NestoAPI#438), sin base de datos, para poder testearlo:
        /// decide QUÉ personas y QUÉ cuentas se copian y con qué números.
        ///
        /// <para>Reglas: no se duplica lo que el destino ya tiene (persona con el mismo correo y
        /// cargo; cuenta con los mismos dígitos), los números nuevos siguen la numeración del
        /// destino, y si el destino no tenía CCC en la ficha se le asigna el equivalente al
        /// predeterminado del principal. Los mandatos SEPA viajan tal cual: el deudor es el mismo
        /// cliente.</para>
        /// </summary>
        internal static ResultadoCopiaDatosPrincipal PrepararCopiaDelPrincipal(
            Cliente principal, Cliente destino, string usuario, DateTime ahora)
        {
            if (principal == null)
            {
                return ResultadoCopiaDatosPrincipal.ConError("El cliente no tiene contacto principal");
            }
            if (destino == null)
            {
                return ResultadoCopiaDatosPrincipal.ConError("No existe el contacto de destino");
            }
            if (destino.ClientePrincipal)
            {
                return ResultadoCopiaDatosPrincipal.ConError("El contacto de destino es el propio principal: no hay nada que copiar");
            }

            ResultadoCopiaDatosPrincipal resultado = new ResultadoCopiaDatosPrincipal();

            // Personas de contacto: se salta las que el destino ya tiene (mismo correo y cargo)
            List<PersonaContactoCliente> personasDestino = (destino.PersonasContactoClientes ?? Enumerable.Empty<PersonaContactoCliente>()).ToList();
            int siguientePersona = SiguienteNumero(personasDestino.Select(p => p.Número));
            foreach (PersonaContactoCliente persona in principal.PersonasContactoClientes ?? Enumerable.Empty<PersonaContactoCliente>())
            {
                bool yaLaTiene = personasDestino.Any(p => p.Cargo == persona.Cargo
                    && string.Equals(p.CorreoElectrónico?.Trim(), persona.CorreoElectrónico?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Nombre?.Trim(), persona.Nombre?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (yaLaTiene)
                {
                    continue;
                }
                resultado.NuevasPersonas.Add(new PersonaContactoCliente
                {
                    Empresa = destino.Empresa,
                    NºCliente = destino.Nº_Cliente,
                    Contacto = destino.Contacto,
                    Número = siguientePersona.ToString(),
                    Nombre = persona.Nombre,
                    Cargo = persona.Cargo,
                    Comentarios = persona.Comentarios,
                    Teléfono = persona.Teléfono,
                    Fax = persona.Fax,
                    CorreoElectrónico = persona.CorreoElectrónico,
                    EnviarBoletin = persona.EnviarBoletin,
                    Estado = persona.Estado,
                    Saludo = persona.Saludo,
                    Usuario = usuario,
                    Fecha_Modificación = ahora
                });
                siguientePersona++;
            }

            // CCC: se salta las cuentas que el destino ya tiene (mismos dígitos)
            List<CCC> cccsDestino = (destino.CCCs ?? Enumerable.Empty<CCC>()).ToList();
            int siguienteCcc = SiguienteNumero(cccsDestino.Select(c => c.Número));
            Dictionary<string, string> numeroDestinoPorNumeroOrigen = new Dictionary<string, string>();
            foreach (CCC ccc in principal.CCCs ?? Enumerable.Empty<CCC>())
            {
                CCC yaLaTiene = cccsDestino.FirstOrDefault(c => MismaCuenta(c, ccc));
                if (yaLaTiene != null)
                {
                    numeroDestinoPorNumeroOrigen[ccc.Número?.Trim() ?? string.Empty] = yaLaTiene.Número;
                    continue;
                }
                CCC copia = new CCC
                {
                    Empresa = destino.Empresa,
                    Cliente = destino.Nº_Cliente,
                    Contacto = destino.Contacto,
                    Número = siguienteCcc.ToString(),
                    Pais = ccc.Pais,
                    DC_IBAN = ccc.DC_IBAN,
                    Entidad = ccc.Entidad,
                    Oficina = ccc.Oficina,
                    DC = ccc.DC,
                    Nº_Cuenta = ccc.Nº_Cuenta,
                    BIC = ccc.BIC,
                    Estado = ccc.Estado,
                    TipoMandato = ccc.TipoMandato,
                    FechaMandato = ccc.FechaMandato,
                    Secuencia = ccc.Secuencia,
                    Usuario = usuario,
                    Fecha_Modificación = ahora
                };
                resultado.NuevosCccs.Add(copia);
                numeroDestinoPorNumeroOrigen[ccc.Número?.Trim() ?? string.Empty] = copia.Número;
                siguienteCcc++;
            }

            // La ficha del destino apunta al equivalente del CCC predeterminado del principal,
            // pero SOLO si no tenía ya uno puesto: lo que haya elegido alguien no se pisa.
            if (string.IsNullOrWhiteSpace(destino.CCC) && !string.IsNullOrWhiteSpace(principal.CCC)
                && numeroDestinoPorNumeroOrigen.TryGetValue(principal.CCC.Trim(), out string equivalente))
            {
                resultado.CccAsignado = equivalente;
            }

            return resultado;
        }

        /// <summary>Los números de personas/CCC son cadenas con números dentro ("1", "2"...): el
        /// siguiente libre es el mayor numérico + 1 (los no numéricos se ignoran).</summary>
        private static int SiguienteNumero(IEnumerable<string> numeros)
        {
            int mayor = 0;
            foreach (string numero in numeros ?? Enumerable.Empty<string>())
            {
                if (int.TryParse(numero?.Trim(), out int valor) && valor > mayor)
                {
                    mayor = valor;
                }
            }
            return mayor + 1;
        }

        private static bool MismaCuenta(CCC una, CCC otra)
        {
            return string.Equals(ClaveCuenta(una), ClaveCuenta(otra), StringComparison.OrdinalIgnoreCase);
        }

        private static string ClaveCuenta(CCC ccc)
        {
            return $"{ccc.Pais?.Trim()}|{ccc.DC_IBAN?.Trim()}|{ccc.Entidad?.Trim()}|{ccc.Oficina?.Trim()}|{ccc.DC?.Trim()}|{ccc.Nº_Cuenta?.Trim()}";
        }

        public async Task<string> ObtenerEmailVendedor(string empresa, string vendedor)
        {
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                return null;
            }

            using (NVEntities db = new NVEntities())
            {
                var vendedorEntity = await db.Vendedores
                    .SingleOrDefaultAsync(v => v.Empresa == empresa && v.Número == vendedor)
                    .ConfigureAwait(false);

                return vendedorEntity?.Mail?.Trim();
            }
        }

        internal static string NormalizarNif(string nif)
        {
            if (string.IsNullOrWhiteSpace(nif))
            {
                return string.Empty;
            }

            // Limpieza general
            nif = nif.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");

            // Issue #285: un NIF de solo guiones/espacios queda vacío tras la limpieza y
            // nif.First() lanzaría 'Sequence contains no elements'.
            if (nif.Length == 0)
            {
                return string.Empty;
            }

            // Detectamos si empieza o acaba con letra
            bool empiezaLetra = char.IsLetter(nif.First());
            bool acabaLetra = char.IsLetter(nif.Last());

            string prefijo = empiezaLetra ? nif.First().ToString() : string.Empty;
            string sufijo = acabaLetra ? nif.Last().ToString() : string.Empty;

            // Parte numérica central (quitando letras)
            string parteNumerica = nif;
            if (empiezaLetra)
            {
                parteNumerica = parteNumerica.Substring(1);
            }

            if (acabaLetra && parteNumerica.Length > 0)
            {
                parteNumerica = parteNumerica.Substring(0, parteNumerica.Length - 1);
            }

            // Quitar ceros iniciales de la parte numérica
            parteNumerica = parteNumerica.TrimStart('0');

            return prefijo + parteNumerica + sufijo;
        }
    }

    /// <summary>
    /// NestoAPI#438: lo que sale de la copia de datos del principal a otro contacto. Las listas
    /// son el plan que la parte con base de datos aplica; los llamantes de la API solo miran los
    /// contadores y el error.
    /// </summary>
    public class ResultadoCopiaDatosPrincipal
    {
        public string Error { get; set; }
        public string CccAsignado { get; set; }

        internal List<PersonaContactoCliente> NuevasPersonas { get; } = new List<PersonaContactoCliente>();
        internal List<CCC> NuevosCccs { get; } = new List<CCC>();

        public int PersonasCopiadas => NuevasPersonas.Count;
        public int CccsCopiados => NuevosCccs.Count;

        public static ResultadoCopiaDatosPrincipal ConError(string error)
        {
            return new ResultadoCopiaDatosPrincipal { Error = error };
        }
    }
}