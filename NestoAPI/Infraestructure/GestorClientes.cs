using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;

namespace NestoAPI.Infraestructure
{
    public class GestorClientes : IGestorClientes
    {
        readonly IServicioGestorClientes servicio;
        readonly IServicioAgencias servicioAgencias;
        public GestorClientes()
        {
            servicio = new ServicioGestorClientes();
            servicioAgencias = new ServicioAgencias();
        }
        public GestorClientes(IServicioGestorClientes servicio, IServicioAgencias servicioAgencias)
        {
            this.servicio = servicio;
            this.servicioAgencias = servicioAgencias;
        }

        public async Task<RespuestaDatosGeneralesClientes> ComprobarDatosGenerales(string direccion, string codigoPostal, string telefono)
        {
            if (String.IsNullOrWhiteSpace(direccion))
            {
                throw new ArgumentException("La dirección no puede estar en blanco");
            }

            //¿Qué ocurre si el Código postal no existe?

            if(string.IsNullOrWhiteSpace(codigoPostal))
            {
                throw new ArgumentException("El código postal no puede estar en blanco");
            }

            RespuestaDatosGeneralesClientes respuesta = await servicio.CogerDatosCodigoPostal(codigoPostal);

            string direccionProcesar = ProcesarDireccion(direccion, respuesta);
            RespuestaAgencia respuestaAgencia = await servicioAgencias.LeerDireccionGoogleMaps(direccionProcesar, codigoPostal);

            respuesta.DireccionFormateada = LimpiarDireccion(direccion, respuestaAgencia.DireccionFormateada, codigoPostal);
            respuesta.TelefonoFormateado = LimpiarTelefono(telefono);

            return respuesta;
        }

        public async Task<RespuestaNifNombreCliente> ComprobarNifNombre(string nif, string nombre)
        {
            if (String.IsNullOrWhiteSpace(nombre))
            {
                throw new ArgumentException("El nombre no puede estar en blanco");
            }

            nif = LimpiarNif(nif);
                        
            var respuesta = new RespuestaNifNombreCliente();
            if (String.IsNullOrWhiteSpace(nif))
            {
                respuesta.EstadoCliente = Constantes.Clientes.Estados.PRIMERA_VISITA;
                respuesta.NombreFormateado = nombre.ToUpper().Trim();
                return respuesta;
            }
            ClienteDTO clienteEncontrado = await servicio.BuscarClientePorNif(nif);
            if(clienteEncontrado != null && clienteEncontrado.cliente != null)
            {
                respuesta.ExisteElCliente = true;
                respuesta.NumeroCliente = clienteEncontrado.cliente;
                respuesta.NombreFormateado = clienteEncontrado.nombre;
                respuesta.NifFormateado = clienteEncontrado.cifNif;
                respuesta.NifValidado = true;
            } else
            {
                respuesta = await servicio.ComprobarNifNombre(nif, nombre);
                respuesta.ExisteElCliente = false;
            }
            return respuesta;
        }

        private string LimpiarNif(string param)
        {
            var resultado = System.Text.RegularExpressions.Regex.Replace(param, "[^a-zA-Z0-9_]+", "");
            return resultado.Trim();
        }

        public string LimpiarDireccion(string direccion, string direccionGoogle, string codigoPostal)
        {
            direccion = direccion.ToUpper().Trim();
            direccionGoogle = direccionGoogle.ToUpper().Trim();

            var posicionCodigoPostal = direccionGoogle.LastIndexOf(codigoPostal);
            // Si es -1 el código postal es incorrecto
            if (posicionCodigoPostal == -1)
            {
                throw new ArgumentException("El código postal " + codigoPostal + " es incorrecto.\n" + direccionGoogle);
            }
            var direccionFormateada = direccionGoogle.Substring(0, posicionCodigoPostal - 2); // porque quitamos coma y espacio

            var posicionComaFormateada = direccionFormateada.IndexOf(", ");
            string numeroCalleFormateada;
            // Si es -1 es porque no hay coma: buscamos el primer numero
            if (posicionComaFormateada == -1)
            {
                string direccionHastaNumero = new string(direccionFormateada
                    .TakeWhile(x => !Char.IsNumber(x)).ToArray());
                numeroCalleFormateada = new string(
                    direccionFormateada.Substring(direccionHastaNumero.Length)
                    .TakeWhile(x => Char.IsNumber(x)).ToArray()
                );
                if (numeroCalleFormateada == "")
                {
                    numeroCalleFormateada = direccion.IndexOf("S/N") != -1 ? "S/N" : "";
                }
                direccionHastaNumero = direccionHastaNumero?.Trim();
                direccionFormateada = direccionHastaNumero.Substring(0, direccionHastaNumero.Length) + 
                    ", " + numeroCalleFormateada;
            } else
            {
                numeroCalleFormateada = direccionFormateada.Substring(posicionComaFormateada + 2);
            }

            var posicionComaEnNumero = numeroCalleFormateada.IndexOf(",");
            if (posicionComaEnNumero != -1)
            {
                direccionFormateada = direccionFormateada.Substring(0, direccionFormateada.Length - numeroCalleFormateada.Length + posicionComaEnNumero);
                numeroCalleFormateada = numeroCalleFormateada.Substring(0, posicionComaEnNumero);
            }
            var posicionNumero = direccion.IndexOf(numeroCalleFormateada);
            //Si posicionNumero es -1, buscar el número anterior (porque tiene dos)
            if (posicionNumero == -1)
            {
                posicionComaFormateada = direccionFormateada.Substring(0, posicionComaFormateada).LastIndexOf(", ");
                if (posicionComaFormateada != -1)
                {
                    numeroCalleFormateada = direccionFormateada.Substring(posicionComaFormateada + 2, direccionFormateada.Length - numeroCalleFormateada.Length - posicionComaFormateada - 4);
                    posicionNumero = direccion.IndexOf(numeroCalleFormateada);
                }
            }
            var inicioDireccion = direccion.Substring(0, posicionNumero + numeroCalleFormateada.Length);
            var finalDireccion = direccion.Substring(inicioDireccion.Length);

            if (posicionNumero == -1)
            {
                direccion = direccionFormateada + "-" + direccion;
            }
            else
            {
                direccion = direccionFormateada + finalDireccion;
            }

            direccion = direccion.Replace("º ", "º");
            direccion = PonerAbreviaturas(direccion);

            return direccion;
        }

        private static string PonerAbreviaturas(string direccion)
        {
            // CALLE
            if (direccion.StartsWith("CALLE DE LA "))
            {
                direccion = "C/ " + direccion.Substring(12);
            }
            if (direccion.StartsWith("CALLE DE "))
            {
                direccion = "C/ " + direccion.Substring(9);
            }
            if (direccion.StartsWith("CALLE "))
            {
                direccion = "C/ " + direccion.Substring(6);
            }
            if (direccion.StartsWith("C/ DE LA "))
            {
                direccion = "C/ " + direccion.Substring(9);
            }
            if (direccion.StartsWith("C/ DEL "))
            {
                direccion = "C/ " + direccion.Substring(7);
            }

            // Avenida
            if (direccion.StartsWith("AVENIDA DE LA "))
            {
                direccion = "Av. " + direccion.Substring(14);
            }
            if (direccion.StartsWith("AVENIDA DE "))
            {
                direccion = "Av. " + direccion.Substring(11);
            }
            if (direccion.StartsWith("AVENIDA "))
            {
                direccion = "Av. " + direccion.Substring(8);
            }
            if (direccion.StartsWith("AV. DE LA "))
            {
                direccion = "Av. " + direccion.Substring(10);
            }
            if (direccion.StartsWith("AV. DE "))
            {
                direccion = "Av. " + direccion.Substring(7);
            }
            if (direccion.StartsWith("AV. "))
            {
                direccion = "Av. " + direccion.Substring(4);
            }
            // Plaza
            if (direccion.StartsWith("PLAZA DE LA "))
            {
                direccion = "Pl. " + direccion.Substring(12);
            }
            if (direccion.StartsWith("PLAZA DE "))
            {
                direccion = "Pl. " + direccion.Substring(9);
            }
            if (direccion.StartsWith("PLAZA "))
            {
                direccion = "Pl. " + direccion.Substring(6);
            }
            
            Dictionary<string, string> abreviaturas = CargarAbreviaturas();
            foreach (var abr in abreviaturas)
            {
                string buscar = abr.Key.ToUpper();
                direccion = direccion.Replace(buscar + " ", abr.Value);
                if (direccion.EndsWith(buscar))
                {
                    direccion = direccion.Substring(0, direccion.Length - buscar.Length) + abr.Value;
                }
            }
            
            return direccion;
        }

        private static Dictionary<string, string> CargarAbreviaturas()
        {
            return new Dictionary<string, string>
            {
                { "Arroyo", "Arr." },
                { "Avenida", "Av." },
                { "Bajada", "Bda." },
                { "Barrio", "Bº" },
                { "Bloque", "Bloq." },
                { "Callejón", "Cjón." },
                { "Camino", "Cº" },
                { "Carretera", "Ctra." },
                { "Centro Comercial", "C.Cial." },
                { "Colonia", "Col." },
                { "Costanilla", "Cost." },
                { "Cuesta", "Cta." },
                { "Doctor", "Dr." },
                { "Duplicado", "dup." },
                { "Escalinta", "Escta." },
                { "Galería", "Gal." },
                { "General", "Gral." },
                { "Glorieta", "Gta." },
                { "Nuestra", "Ntra." },
                { "Pasadizo", "Pzo." },
                { "Pasaje", "Pje." },
                { "Paseo", "Pº" },
                { "Plaza", "Pl." },
                { "Poblado", "Pobl." },
                { "Postigo", "Pgo." },
                { "Provincia", "Pv." },
                { "Ronda", "Rda." },
                { "Santa", "Sta." },
                { "Santo", "Sto." },
                { "Senda", "Sa." },
                { "Señora", "Sra." },
                { "Subida", "Sda." },
                { "Travesía", "Trv." },
                { "Urbanización", "Urb." }
            };
        }

        private string ProcesarDireccion(string direccion, RespuestaDatosGeneralesClientes respuesta )
        {
            string direccionRespuesta = direccion.Replace("  ", " ") + "+";
            //direccionRespuesta += respuesta.CodigoPostal + "+";
            direccionRespuesta += respuesta.Poblacion;
            if (respuesta.Poblacion?.ToUpper().Trim() != respuesta.Provincia?.ToUpper().Trim())
            {
                direccionRespuesta += "+" +respuesta.Provincia;
            }
            direccionRespuesta = Regex.Replace(direccionRespuesta, @"\s+", " ");
            direccionRespuesta = direccionRespuesta.Replace(" ", "+");
            return direccionRespuesta;
        }

        public RespuestaDatosBancoCliente ComprobarDatosBanco(string formaPago, string plazosPago, string ibanComprobar)
        {
            RespuestaDatosBancoCliente respuesta;
            if (ibanComprobar != null)
            {
                Iban iban = new Iban(ibanComprobar);
                respuesta = new RespuestaDatosBancoCliente
                {
                    Iban = iban.Codigo,
                    IbanFormateado = iban.Formateado,
                    IbanValido = iban.EsValido
                };
            } else
            {
                respuesta = new RespuestaDatosBancoCliente
                {
                    Iban = "",
                    IbanFormateado = "",
                    IbanValido = true
                };
            }
            if ((plazosPago == "CONTADO" && formaPago != "RCB") || (formaPago == "RCB" && respuesta.IbanValido && (plazosPago == "1/30" || plazosPago == "CONTADO")))
            {
                respuesta.DatosPagoValidos = true;
            } else
            {
                respuesta.DatosPagoValidos = false;
            }

            return respuesta;
        }

        public string LimpiarTelefono(string telefono)
        {
            string telefonoFormateado = "";
            foreach(var ch in telefono)
            {
                if (Char.IsDigit(ch))
                {
                    telefonoFormateado += ch;
                }
            }

            if (telefonoFormateado.Length > 9)
            {
                telefonoFormateado = telefonoFormateado.Substring(0, 9) + "/" + telefonoFormateado.Substring(9);
            }
            if (telefonoFormateado.Length > 19)
            {
                telefonoFormateado = telefonoFormateado.Substring(0, 19) + "/" + telefonoFormateado.Substring(19);
            }

            return telefonoFormateado;
        }

        public async Task<ClienteCrear> ConstruirClienteCrear(string empresa, string cliente, string contacto)
        {
            Cliente clienteDb = await servicio.BuscarCliente(empresa, cliente, contacto);
            ClienteCrear clienteCrear = new ClienteCrear
            {
                Empresa = clienteDb.Empresa.Trim(),
                Cliente = clienteDb.Nº_Cliente.Trim(),
                Contacto = clienteDb.Contacto.Trim(),
                CodigoPostal = clienteDb.CodPostal?.Trim(),
                Direccion = clienteDb.Dirección?.Trim(),
                Estado = clienteDb.Estado,
                Nif = clienteDb.CIF_NIF?.Trim(),
                Nombre = clienteDb.Nombre?.Trim(),
                Poblacion = clienteDb.Población?.Trim(),
                Provincia = clienteDb.Provincia?.Trim(),
                Ruta = clienteDb.Ruta?.Trim(),
                Telefono = clienteDb.Teléfono?.Trim(),
                VendedorEstetica = clienteDb.Vendedor?.Trim(),
                EsContacto = !clienteDb.ClientePrincipal
            };

            VendedorClienteGrupoProducto vendedorGrupo = await servicio.BuscarVendedorGrupo(empresa, cliente, contacto, Constantes.Productos.GRUPO_PELUQUERIA);
            if (vendedorGrupo != null)
            {
                clienteCrear.VendedorPeluqueria = vendedorGrupo.Vendedor?.Trim();
            }
            clienteCrear.Estetica = clienteCrear.VendedorEstetica != null && clienteCrear.VendedorEstetica != Constantes.Vendedores.VENDEDOR_GENERAL;
            clienteCrear.Peluqueria = clienteCrear.VendedorPeluqueria != null && clienteCrear.VendedorPeluqueria != Constantes.Vendedores.VENDEDOR_GENERAL;

            CondPagoCliente condPagoCliente = await servicio.BuscarCondicionesPago(empresa, cliente, contacto);
            clienteCrear.FormaPago = condPagoCliente.FormaPago;
            clienteCrear.PlazosPago = condPagoCliente.PlazosPago;

            CCC cccCliente = await servicio.BuscarCCC(empresa, cliente, contacto, clienteDb.CCC);
            clienteCrear.Iban = cccCliente.Pais + cccCliente.DC_IBAN + cccCliente.Entidad + cccCliente.Oficina + cccCliente.DC + cccCliente.Nº_Cuenta;            

            return clienteCrear;
        }
    }
}