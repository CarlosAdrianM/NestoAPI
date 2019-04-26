using System;
using System.Collections.Generic;
using System.Linq;
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
            RespuestaAgencia respuestaAgencia = await servicioAgencias.LeerDireccionGoogleMaps(direccionProcesar);

            respuesta.DireccionFormateada = LimpiarDireccion(direccion, respuestaAgencia.DireccionFormateada, codigoPostal);
            respuesta.TelefonoFormateado = telefono;

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

        private string LimpiarDireccion(string direccion, string direccionGoogle, string codigoPostal)
        {
            direccion = direccion.ToUpper().Trim();
            direccionGoogle = direccionGoogle.ToUpper().Trim();

            var posicionCodigoPostal = direccionGoogle.LastIndexOf(codigoPostal);
            // Si es -1 el código postal es incorrecto
            if (posicionCodigoPostal == -1)
            {
                throw new ArgumentException("El código postal " + codigoPostal + " es incorrecto.\n"+direccionGoogle);
            }
            var direccionFormateada = direccionGoogle.Substring(0, posicionCodigoPostal - 2); // porque quitamos coma y espacio

            var posicionComaFormateada = direccionFormateada.IndexOf(", ");
            var numeroCalleFormateada = direccionFormateada.Substring(posicionComaFormateada + 2);
            var posicionComaEnNumero = numeroCalleFormateada.IndexOf(",");
            if(posicionComaEnNumero != -1)
            {
                direccionFormateada = direccionFormateada.Substring(0, direccionFormateada.Length - numeroCalleFormateada.Length + posicionComaEnNumero);
                numeroCalleFormateada = numeroCalleFormateada.Substring(0, posicionComaEnNumero);
            }
            var posicionNumero = direccion.IndexOf(numeroCalleFormateada);
            //Si posicionNumero es -1, buscar el número anterior (porque tiene dos)
            if (posicionNumero == -1)
            {
                posicionComaFormateada = direccionFormateada.Substring(0, posicionComaFormateada).LastIndexOf(", ");
                if (posicionComaFormateada != -1) {
                    numeroCalleFormateada = direccionFormateada.Substring(posicionComaFormateada + 2, direccionFormateada.Length - numeroCalleFormateada.Length - posicionComaFormateada - 4);
                    posicionNumero = direccion.IndexOf(numeroCalleFormateada);
                }
            }
            var inicioDireccion = direccion.Substring(0, posicionNumero + numeroCalleFormateada.Length);
            var finalDireccion = direccion.Substring(inicioDireccion.Length);

            if (posicionNumero == -1)
            {
                direccion = direccionFormateada + "-" + direccion;
            } else
            {
                direccion = direccionFormateada + finalDireccion;
            }




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
            /*
            if (direccion.StartsWith("C /"))
            {
                direccion = "C/" + direccion.Substring(3);
            }
            if (direccion.StartsWith("C/") && !direccion.StartsWith("C/ "))
            {
                direccion = "C/ " + direccion.Substring(2);
            }

            // NUMERO
            var posicionComa = direccion.LastIndexOf(",");
            if (posicionComa != -1 && direccion.Substring(posicionComa+1, 1) != " ")
            {
                direccion = direccion.Substring(0, posicionComa+1) + " " + direccion.Substring(posicionComa + 1);
            }
            var posicionNumero = direccion.IndexOfAny("0123456789".ToCharArray());
            if (direccion.Substring(posicionNumero-1,1) == " " && direccion.Substring(posicionNumero - 2, 1) != ",")
            {
                direccion = direccion.Substring(0, posicionNumero - 1) + ", " + direccion.Substring(posicionNumero);
            }
            */

            return direccion;
        }

        private string ProcesarDireccion(string direccion, RespuestaDatosGeneralesClientes respuesta )
        {
            string direccionRespuesta = direccion + "+";
            direccionRespuesta += respuesta.CodigoPostal + "+";
            direccionRespuesta += respuesta.Poblacion + "+";
            direccionRespuesta += respuesta.Provincia + "+";
            direccionRespuesta += "España";
            direccionRespuesta = direccionRespuesta.Replace(" ", "+");
            return direccionRespuesta;
        }

    }
}