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
            Cliente clienteDevolver = await db.Clientes.Include(c => c.CondPagoClientes)
                .Include(c => c.CCC1).Include(c => c.Vendedore).Include(c => c.PersonasContactoClientes)
                .SingleAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto);

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

    }
}