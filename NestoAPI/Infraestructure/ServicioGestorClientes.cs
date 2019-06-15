using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
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
            NVEntities db = new NVEntities();
            Cliente cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.ClientePrincipal == true && c.CIF_NIF == nif);
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

        public async Task<RespuestaNifNombreCliente> ComprobarNifNombre(string nif, string nombre)
        {
            byte[] bytesNif = Encoding.Default.GetBytes(nif?.ToUpper().Trim());
            nif = Encoding.UTF8.GetString(bytesNif);

            byte[] bytesNombre = Encoding.Default.GetBytes(nombre?.ToUpper().Trim());
            nombre = Encoding.UTF8.GetString(bytesNombre);

            HttpWebRequest request = CreateWebRequest();
            XmlDocument soapEnvelopeXml = new XmlDocument();
            soapEnvelopeXml.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
                <soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:vnif=""http://www2.agenciatributaria.gob.es/static_files/common/internet/dep/aplicaciones/es/aeat/burt/jdit/ws/VNifV2Ent.xsd"">
                <soapenv:Header/>
                <soapenv:Body>
                    <vnif:VNifV2Ent>
                        <vnif:Contribuyente>
                            <vnif:Nif> " +nif+ @" </vnif:Nif>
                            <vnif:Nombre> " +nombre+ @" </vnif:Nombre>
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
            using (WebResponse response = await request.GetResponseAsync())
            {
                using (StreamReader rd = new StreamReader(response.GetResponseStream()))
                {
                    string soapResult = rd.ReadToEnd();
                    XmlDocument xml = new XmlDocument();
                    xml.LoadXml(soapResult);
                    var nsmgr = new XmlNamespaceManager(xml.NameTable);
                    nsmgr.AddNamespace("VNifV2Sal", "http://www2.agenciatributaria.gob.es/static_files/common/internet/dep/aplicaciones/es/aeat/burt/jdit/ws/VNifV2Sal.xsd");
                    var contribuyente = xml.DocumentElement.FirstChild.FirstChild.FirstChild;
                    nifDevuelto = contribuyente.SelectSingleNode("VNifV2Sal:Nif", nsmgr).InnerText;
                    nombreDevuelto = contribuyente.SelectSingleNode("VNifV2Sal:Nombre", nsmgr).InnerText;
                    resultadoDevuelto = contribuyente.SelectSingleNode("VNifV2Sal:Resultado", nsmgr).InnerText;
                }
            }

            if (resultadoDevuelto.ToUpper()=="IDENTIFICADO-BAJA")
            {
                nombreDevuelto = "¡EMPRESA DE BAJA! " + nombreDevuelto;
            }

            return new RespuestaNifNombreCliente {
                NifFormateado = nifDevuelto?.Trim(),
                NombreFormateado = nombreDevuelto?.Trim(),
                NifValidado = resultadoDevuelto?.ToUpper() == "IDENTIFICADO" || 
                resultadoDevuelto?.ToUpper() == "NO IDENTIFICADO-SIMILAR" ||
                resultadoDevuelto?.ToUpper() == "IDENTIFICADO-BAJA"
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

                var respuesta = new RespuestaDatosGeneralesClientes
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
            string pathApp = AppDomain.CurrentDomain.BaseDirectory;
            //string certName = @"C:\Users\Administrador.NUEVAVISION\source\repos\NestoAPI\NestoAPI\Infraestructure\Certificados\cert_cam_nv.pfx";
            string fileName = "cert_cam_nv.pfx";
            string certName = Path.Combine(pathApp, @"Infraestructure\Certificados\", fileName);
            string password = ConfigurationManager.AppSettings["CertificadoDigital"];
            string host = @"https://www1.agenciatributaria.gob.es/wlpl/BURT-JDIT/ws/VNifV2SOAP";

            X509Certificate2Collection certificates = new X509Certificate2Collection();
            certificates.Import(certName, password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

            ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;

            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(host);
            webRequest.AllowAutoRedirect = true;
            webRequest.ClientCertificates = certificates;

            webRequest.Headers.Add(@"SOAP:Action");
            webRequest.ContentType = "text/xml;charset=\"utf-8\"";
            webRequest.Accept = "text/xml";
            webRequest.Method = "POST";
            return webRequest;
        }

        public async Task<Cliente> PrepararCliente(ClienteCrear clienteCrear)
        {
            if (clienteCrear.EsContacto)
            {
                throw new NotImplementedException("No se pueden crear contactos aún");
            }

            string contacto = "0"; //calcular
            
            Cliente cliente = new Cliente
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Nº_Cliente = clienteCrear.Cliente,
                Contacto = contacto,

                CIF_NIF = clienteCrear.Nif,
                ClientePrincipal = !clienteCrear.EsContacto,
                CodPostal = clienteCrear.CodigoPostal,
                ContactoBonificacion = contacto,
                ContactoCobro = contacto,
                ContactoDefecto = contacto,
                DiasEnServir = Constantes.Clientes.DIAS_EN_SERVIR_POR_DEFECTO,
                Dirección = clienteCrear.Direccion,
                Estado = clienteCrear.Estado,
                Grupo = Constantes.Clientes.GRUPO_POR_DEFECTO,
                IVA = Constantes.Empresas.IVA_POR_DEFECTO,
                Nombre = clienteCrear.Nombre?.ToUpper(),
                PeriodoFacturación = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Población = clienteCrear.Poblacion,
                Provincia = clienteCrear.Provincia,
                Ruta = clienteCrear.Ruta,
                ServirJunto = true,
                Teléfono = clienteCrear.Telefono,
                Vendedor = clienteCrear.Estetica ? clienteCrear.VendedorEstetica : Constantes.Vendedores.VENDEDOR_GENERAL,
                Usuario = clienteCrear.Usuario
            };

            if (clienteCrear.VendedorPeluqueria != null && clienteCrear.VendedorPeluqueria != clienteCrear.VendedorEstetica)
            {
                cliente.VendedoresClienteGrupoProductoes.Add(new VendedorClienteGrupoProducto
                {
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Cliente = clienteCrear.Cliente,
                    Contacto = contacto,
                    Vendedor = clienteCrear.Peluqueria ? clienteCrear.VendedorPeluqueria : Constantes.Vendedores.VENDEDOR_GENERAL,
                    GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA,
                    Usuario = clienteCrear.Usuario
                });
            }
            
            int i = 1;
            foreach (PersonaContactoDTO personaCrear in clienteCrear.PersonasContacto.Where(p=> !string.IsNullOrEmpty(p.Nombre) ||  !string.IsNullOrEmpty(p.CorreoElectronico)))
            {
                PersonaContactoCliente persona = new PersonaContactoCliente
                {
                    Empresa = cliente.Empresa,
                    Cliente = cliente,
                    Número = i++.ToString(),
                    Cargo = Constantes.Clientes.CARGO_POR_DEFECTO,
                    Nombre = personaCrear.Nombre,
                    CorreoElectrónico = personaCrear.CorreoElectronico,
                    EnviarBoletin = true,
                    Estado = 0,
                    Usuario = clienteCrear.Usuario
                };
                cliente.PersonasContactoClientes.Add(persona);
            }
            
            CondPagoCliente condicionesPago = new CondPagoCliente
            {
                Empresa = cliente.Empresa,
                Cliente = cliente,
                ImporteMínimo = 0,
                FormaPago = clienteCrear.FormaPago,
                PlazosPago = clienteCrear.PlazosPago
            };
            cliente.CondPagoClientes.Add(condicionesPago);

            if (clienteCrear.Iban != null && clienteCrear.Iban!="")
            {
                CCC ccc = new CCC
                {
                    Empresa = cliente.Empresa,
                    Cliente1 = cliente,
                    Número = "1",
                    Pais = clienteCrear.Iban.Substring(0, 2),
                    DC_IBAN = clienteCrear.Iban.Substring(2, 2),
                    Entidad = clienteCrear.Iban.Substring(5, 4),
                    Oficina = clienteCrear.Iban.Substring(10, 4),
                    DC = clienteCrear.Iban.Substring(15, 2),
                    Nº_Cuenta = clienteCrear.Iban.Substring(17, 2)
                    + clienteCrear.Iban.Substring(20, 4)
                    + clienteCrear.Iban.Substring(25, 4),
                    Estado = Constantes.Clientes.EstadosMandatos.EN_PODER_DEL_CLIENTE,
                    Secuencia = Constantes.Clientes.SECUENCIA_POR_DEFECTO,
                    Usuario = clienteCrear.Usuario
                };
                cliente.CCCs.Add(ccc);
            }
            
            return cliente;

        }

        public Task<CCC> PrepararCCC(ClienteCrear clienteCrear)
        {
            throw new NotImplementedException();
        }
    }
}