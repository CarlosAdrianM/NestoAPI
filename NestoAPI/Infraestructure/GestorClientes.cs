﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System.Globalization;
using Microsoft.Reporting.WebForms;
using System.Net.Http;
using System.Data.SqlClient;
using System.IO;
using Microsoft.ML;
using System.Data.Entity;
using static NestoAPI.Models.Constantes;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models.Sincronizacion;
using Newtonsoft.Json;
using Microsoft.Ajax.Utilities;

namespace NestoAPI.Infraestructure
{
    public class GestorClientes : IGestorClientes
    {
        readonly IServicioGestorClientes servicio;
        readonly IServicioAgencias servicioAgencias;
        private readonly SincronizacionEventWrapper _sincronizacionEventWrapper;

        public GestorClientes(IServicioGestorClientes servicio, IServicioAgencias servicioAgencias, SincronizacionEventWrapper sincronizacionEventWrapper)
        {
            this.servicio = servicio;
            this.servicioAgencias = servicioAgencias;
            _sincronizacionEventWrapper = sincronizacionEventWrapper;
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

            String[] listaTelefonos = respuesta.TelefonoFormateado.Split(Constantes.Clientes.SEPARADOR_TELEFONOS);
            respuesta.ClientesMismoTelefono = new List<ClienteTelefonoLookup>();

            foreach (var tel in listaTelefonos) 
            {
                respuesta.ClientesMismoTelefono.AddRange(await servicio.ClientesMismoTelefono(tel));
            }

            respuesta.ClientesMismoTelefono = respuesta.ClientesMismoTelefono.Distinct().ToList();
            
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
                respuesta.Empresa = clienteEncontrado.empresa;
                respuesta.NumeroCliente = clienteEncontrado.cliente;
                respuesta.Contacto = clienteEncontrado.contacto;
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
            var resultado = Regex.Replace(param, "[^a-zA-Z0-9_]+", string.Empty);
            return resultado.Trim();
        }

        public string LimpiarDireccion(string direccion, string direccionGoogle, string codigoPostal)
        {
            direccion = direccion.ToUpper().Trim();
            codigoPostal = codigoPostal.Trim();
            direccionGoogle = direccionGoogle.ToUpper().Trim();

            var posicionCodigoPostal = direccionGoogle.LastIndexOf(codigoPostal);
            // Si es -1 el código postal es incorrecto
            if (posicionCodigoPostal == -1)
            {
                throw new ArgumentException("El código postal " + codigoPostal + " es incorrecto.\n" + direccionGoogle);
            }
            var direccionFormateada = string.Empty;
            if (posicionCodigoPostal >= 2)
            {
                direccionFormateada = direccionGoogle.Substring(0, posicionCodigoPostal - 2); // porque quitamos coma y espacio
            }
            

            var posicionComaFormateada = direccionFormateada.IndexOf(", ", StringComparison.InvariantCulture);
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
                if (string.IsNullOrEmpty(numeroCalleFormateada))
                {
                    numeroCalleFormateada = direccion.IndexOf("S/N", StringComparison.InvariantCulture) != -1 ? "S/N" : string.Empty;
                }
                if (string.IsNullOrEmpty(numeroCalleFormateada) && direccionGoogle.Substring(0,5) != codigoPostal)
                {
                    Match m = Regex.Match(direccion, "(\\d+)");
                    if (m.Success)
                    {
                        numeroCalleFormateada = m.Value;
                    }
                }
                direccionHastaNumero = direccionHastaNumero?.Trim();
                if (!string.IsNullOrEmpty(direccionFormateada))
                {
                    direccionFormateada = direccionHastaNumero.Substring(0, direccionHastaNumero.Length) +
                        ", " + numeroCalleFormateada;
                }
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
            // También puede ser porque no tenga ninguno
            if (posicionNumero == -1)
            {
                posicionComaFormateada = direccionFormateada.Substring(0, posicionComaFormateada).LastIndexOf(", ");
                if (posicionComaFormateada != -1)
                {
                    numeroCalleFormateada = direccionFormateada.Substring(posicionComaFormateada + 2, direccionFormateada.Length - numeroCalleFormateada.Length - posicionComaFormateada - 4);
                    posicionNumero = direccion.IndexOf(numeroCalleFormateada);
                }
                else
                {
                    string sinNumero = "S/N";
                    direccionFormateada = direccionFormateada.Replace(numeroCalleFormateada, sinNumero);
                    numeroCalleFormateada = sinNumero;
                    posicionNumero = direccion.IndexOf(numeroCalleFormateada);                    
                }
            }
            var inicioDireccion = direccion.Substring(0, posicionNumero + numeroCalleFormateada.Length);
            var finalDireccion = direccion.Substring(inicioDireccion.Length);
            if (finalDireccion.Trim() == ",")
            {
                finalDireccion = string.Empty;
            }

            if (posicionNumero == -1 && direccionFormateada.ToUpper() != direccion.ToUpper())
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
            if (direccion.StartsWith("C. "))
            {
                direccion = "C/ " + direccion.Substring(3);
            }
            if (direccion.StartsWith("C/ DE LA "))
            {
                direccion = "C/ " + direccion.Substring(9);
            }
            if (direccion.StartsWith("C/ DEL "))
            {
                direccion = "C/ " + direccion.Substring(7);
            }
            if (direccion.StartsWith("C/ DE "))
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
            
            Dictionary<string, string> abreviaturas = CargarAbreviaturas();
            foreach (var abr in abreviaturas)
            {
                string buscar = abr.Key.ToUpper();
                direccion = direccion.Replace(buscar + " ", abr.Value);
                if (abr.Value != "Sra.")
                {
                    direccion = direccion.Replace(abr.Value + "DE LA ", abr.Value);
                    direccion = direccion.Replace(abr.Value + "DEL ", abr.Value);
                    direccion = direccion.Replace(abr.Value + "DE ", abr.Value);
                }                

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
            if (ibanComprobar != null && ibanComprobar.ToLower() != "null")
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
                    Iban = string.Empty,
                    IbanFormateado = string.Empty,
                    IbanValido = true
                };
            }
            if (((plazosPago == "CONTADO" || plazosPago == "PRE") && formaPago != "RCB") || (formaPago == "RCB" && respuesta.IbanValido && (plazosPago == "1/30" || plazosPago == "CONTADO")))
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
            if (telefono == null)
            {
                return string.Empty;
            }

            string telefonoFormateado = String.Empty;
            
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
                Comentarios = clienteDb.Comentarios?.Trim(),
                ComentariosPicking = clienteDb.ComentarioPicking?.Trim(),
                ComentariosRuta = clienteDb.ComentarioRuta?.Trim(),
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
            clienteCrear.FormaPago = condPagoCliente.FormaPago?.Trim();
            clienteCrear.PlazosPago = condPagoCliente.PlazosPago?.Trim();

            CCC cccCliente = await servicio.BuscarCCC(empresa, cliente, contacto, clienteDb.CCC);
            if (cccCliente != null)
            {
                clienteCrear.Iban = cccCliente.Pais + cccCliente.DC_IBAN + cccCliente.Entidad + cccCliente.Oficina + cccCliente.DC + cccCliente.Nº_Cuenta;
            }

            List<PersonaContactoCliente> personas = await servicio.BuscarPersonasContacto(empresa, cliente, contacto);
            clienteCrear.PersonasContacto = new List<PersonaContactoDTO>();
            foreach (var persona in personas)
            {
                int numeroPersona = 0;
                try
                {
                    numeroPersona = Int32.Parse(persona.Número);
                }
                catch
                {
                    numeroPersona = 1;
                }
                clienteCrear.PersonasContacto.Add(new PersonaContactoDTO
                {
                    Numero = numeroPersona,
                    Nombre = persona.Nombre?.Trim(),
                    CorreoElectronico = persona.CorreoElectrónico?.Trim(),
                    FacturacionElectronica = persona.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO
                });
            }

            return clienteCrear;
        }

        public async Task<ClienteTelefonoLookup> BuscarClientePorEmail(string email)
        {
            return await servicio.BuscarClientePorEmail(email);
        }

        public async Task<List<Cliente>> DejarDeVisitar(NVEntities db, ClienteCrear cliente)
        {
            // En el parámetro "cliente" es donde marcamos el cambio:
            // - Si vendedorEstetica es NV es el vendedor de estética el que se lo quita
            // - Si vendedorPeluqueria es NV es el vendedor de peluquería el que se lo quita
            // No hay riesgo de que el cliente ya fuese de NV, porque solo marcamos el vendedor que se lo quita, no el que tiene actualmente

            List<Cliente> clientesModificados = new List<Cliente>();

            Cliente clienteDB = await servicio.BuscarCliente(db, cliente.Empresa, cliente.Cliente, cliente.Contacto);
            string nuevoVendedor = String.Empty;
            short nuevoEstado = Constantes.Clientes.Estados.VISITA_TELEFONICA;
            List<Cliente> contactos = await servicio.BuscarContactos(db, cliente.Empresa, cliente.Cliente, cliente.Contacto);
            List<string> vendedoresContacto = VendedoresContactosCliente(contactos);

            if (clienteDB.Estado != Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO)
            {

                var vendedoresQueRecibenClientes = await servicio.VendedoresQueRecibenClientes();

                int diaVendedor = servicio.Hoy().Minute % vendedoresQueRecibenClientes.Count;
                nuevoVendedor = vendedoresQueRecibenClientes.Contains(clienteDB.Vendedor) ?
                    clienteDB.Vendedor :
                    vendedoresQueRecibenClientes.ElementAt(diaVendedor);
                               
                if (nuevoVendedor == vendedoresQueRecibenClientes.ElementAt(diaVendedor))
                {
                    var vendedoresContactoTelefonicos = vendedoresContacto.Intersect(vendedoresQueRecibenClientes);
                    if (vendedoresContactoTelefonicos.FirstOrDefault() != null)
                    {
                        nuevoVendedor = vendedoresContactoTelefonicos.First();
                    }
                }
            }
            string vendedorPeluqueria = string.Empty;
            short estadoPeluqueria = Constantes.Clientes.Estados.VISITA_PRESENCIAL;
            List<string> vendedoresPresenciales = await servicio.VendedoresPresenciales();

            // Se lo quita un comercial de estética telefónico
            if (cliente.VendedorEstetica == Constantes.Vendedores.VENDEDOR_GENERAL && clienteDB.Vendedor != Constantes.Vendedores.VENDEDOR_GENERAL && clienteDB.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO)
            {
                nuevoVendedor = Constantes.Vendedores.VENDEDOR_GENERAL;                
                SeguimientoCliente seguimiento = await servicio.BuscarSeguimiento(cliente.Empresa, cliente.Cliente, cliente.Contacto).ConfigureAwait(false);
                if (seguimiento != null && !string.IsNullOrEmpty(seguimiento.Comentarios))
                {
                    bool soloEstetica = CultureInfo.InvariantCulture.CompareInfo.IndexOf(seguimiento.Comentarios, "solo estetica", CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
                    bool soloPeluqueria = CultureInfo.InvariantCulture.CompareInfo.IndexOf(seguimiento.Comentarios, "solo peluqueria", CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
                    bool esteticaYPeluqueria = CultureInfo.InvariantCulture.CompareInfo.IndexOf(seguimiento.Comentarios, "estetica y peluqueria", CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0; 
                    bool pasarANulo = CultureInfo.InvariantCulture.CompareInfo.IndexOf(seguimiento.Comentarios, "pasar a nulo", CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
                    if (soloEstetica)
                    {
                        nuevoEstado = Constantes.Clientes.Estados.SIN_ACCION_COMERCIAL_SOLO_ESTETICA;
                    }
                    else if (soloPeluqueria)
                    {
                        nuevoEstado = Constantes.Clientes.Estados.SIN_ACCION_COMERCIAL_SOLO_PELUQUERIA;
                    }
                    else if (esteticaYPeluqueria)
                    {
                        nuevoEstado = Constantes.Clientes.Estados.SIN_ACCION_COMERCIAL_ESTETICA_Y_PELUQUERIA;
                    }
                    else if (pasarANulo)
                    {
                        nuevoEstado = Constantes.Clientes.Estados.NULO;
                    }
                    else
                    {
                        throw new Exception("Debe especificar en el texto del seguimiento algún texto que ayude a determinar a qué estado se debe pasar el cliente");
                    }
                }
            }

            // Se lo quita un comercial de estética (presencial o telefónico)
            if (cliente.VendedorEstetica == Constantes.Vendedores.VENDEDOR_GENERAL && clienteDB.Vendedor != Constantes.Vendedores.VENDEDOR_GENERAL)
            {
                if (clienteDB.VendedoresClienteGrupoProductoes == null ||
                    clienteDB.VendedoresClienteGrupoProductoes
                        .SingleOrDefault(v => v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA)?
                        .Vendedor?.Trim() == Constantes.Vendedores.VENDEDOR_GENERAL ||
                    clienteDB.VendedoresClienteGrupoProductoes
                        .SingleOrDefault(v => v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA)?
                        .Vendedor?.Trim() == clienteDB.Vendedor)
                {
                    vendedorPeluqueria = nuevoVendedor;
                    estadoPeluqueria = nuevoEstado;
                }

                clienteDB.Vendedor = nuevoVendedor;
                clienteDB.Estado = nuevoEstado;

                contactos = contactos.Where(c => c.Estado == Constantes.Clientes.Estados.COMISIONA_SIN_VISITA).ToList();
                foreach (Cliente contacto in contactos.Where(c => c.Vendedor == clienteDB.Vendedor || !vendedoresPresenciales.Contains(c.Vendedor)))
                {
                    contacto.Vendedor = nuevoVendedor;
                    contacto.Usuario = cliente.Usuario;
                    clientesModificados.Add(contacto);
                }
            }

            // Se lo quita un comercial de peluquería
            if (cliente.VendedorPeluqueria == Constantes.Vendedores.VENDEDOR_GENERAL)
            {              
                vendedoresPresenciales = vendedoresPresenciales.Where(v => !v.Equals(clienteDB.VendedoresClienteGrupoProductoes?.FirstOrDefault().Vendedor)).ToList();
                if (vendedoresPresenciales.Contains(clienteDB.Vendedor))
                {
                    vendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL;
                }
                else
                {
                    vendedoresContacto = vendedoresContacto.Where(v => v != clienteDB.VendedoresClienteGrupoProductoes?.FirstOrDefault().Vendedor).ToList();
                    bool tieneVendedorPresencialEnContactos = vendedoresContacto.Intersect(vendedoresPresenciales).Any();
                    bool noTieneNingunVendedorEnContactos = vendedoresContacto.All(v => v == Constantes.Vendedores.VENDEDOR_GENERAL || v == nuevoVendedor);
                    vendedorPeluqueria = tieneVendedorPresencialEnContactos ? Constantes.Vendedores.VENDEDOR_GENERAL : nuevoVendedor;
                    if (clienteDB.Vendedor == Constantes.Vendedores.VENDEDOR_GENERAL && !tieneVendedorPresencialEnContactos)
                    {
                        clienteDB.Vendedor = nuevoVendedor;
                        clienteDB.Estado = Constantes.Clientes.Estados.VISITA_TELEFONICA;
                        clienteDB.Usuario = cliente.Usuario;
                    }
                    if (tieneVendedorPresencialEnContactos)
                    {
                        foreach (Cliente contacto in contactos)
                        {
                            var vendedorPeluqueriaContacto = contacto.VendedoresClienteGrupoProductoes.FirstOrDefault();
                            if (vendedorPeluqueriaContacto != null)
                            {
                                vendedorPeluqueriaContacto.Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL;
                                contacto.Usuario = cliente.Usuario;
                                clientesModificados.Add(contacto);
                            }
                        }
                    }
                    if (noTieneNingunVendedorEnContactos)
                    {
                        foreach (Cliente contacto in contactos)
                        {
                            contacto.Vendedor = nuevoVendedor;
                            contacto.Usuario = cliente.Usuario;
                            var vendedorPeluqueriaContacto = contacto.VendedoresClienteGrupoProductoes.FirstOrDefault();
                            if (vendedorPeluqueriaContacto != null)
                            {
                                vendedorPeluqueriaContacto.Vendedor = nuevoVendedor;
                                vendedorPeluqueriaContacto.Usuario = cliente.Usuario;
                                clientesModificados.Add(contacto);
                            }
                        }
                    }
                }
            }

            // Si no existe el vendedor de peluquería y lo necesitamos, lo creamos
            if (vendedorPeluqueria != string.Empty)
            {
                if (clienteDB.VendedoresClienteGrupoProductoes.SingleOrDefault(v => v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA) != null)
                {
                    clienteDB.VendedoresClienteGrupoProductoes.SingleOrDefault(v => v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA).Vendedor = vendedorPeluqueria;
                    clienteDB.VendedoresClienteGrupoProductoes.SingleOrDefault(v => v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA).Estado = estadoPeluqueria;
                    clienteDB.VendedoresClienteGrupoProductoes.SingleOrDefault(v => v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA).Usuario = cliente.Usuario;
                }
                else
                {
                    clienteDB.VendedoresClienteGrupoProductoes.Add(new VendedorClienteGrupoProducto
                    {
                        GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA,
                        Vendedor = vendedorPeluqueria,
                        Usuario = cliente.Usuario
                    });
                }
            }            

            clienteDB.Usuario = cliente.Usuario;

            clientesModificados.Add(clienteDB);

            return clientesModificados;
        }

        private List<string> VendedoresContactosCliente(List<Cliente> contactos)
        {
            List<string> vendedoresGrupo = contactos.SelectMany(v => v.VendedoresClienteGrupoProductoes).Select(c => c.Vendedor).ToList();
            var todosVendedores = contactos.Select(c => c.Vendedor).Union(vendedoresGrupo).Distinct();
            return todosVendedores.ToList();
        }

        public async Task<Cliente> PrepararClienteModificar(ClienteCrear clienteModificar, NVEntities db)
        {
            Cliente clienteDB = await servicio.BuscarCliente(db, clienteModificar.Empresa, clienteModificar.Cliente, clienteModificar.Contacto);
            if (clienteDB == null)
            {
                throw new Exception(String.Format("Bad request: no existe el cliente {0}/{1}/{2}", clienteModificar.Empresa, clienteModificar.Cliente, clienteModificar.Contacto));
            }

            //test estado 5 -> 0 o 9
            if ((string.IsNullOrWhiteSpace(clienteDB.CIF_NIF) && !string.IsNullOrWhiteSpace(clienteModificar.Nif)) || 
                (clienteDB.Estado == Constantes.Clientes.Estados.PRIMERA_VISITA) && !string.IsNullOrWhiteSpace(clienteDB.CIF_NIF))
            {
                clienteDB.Estado = clienteDB.Vendedore.Estado == Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO ? 
                    Constantes.Clientes.Estados.VISITA_TELEFONICA : Constantes.Clientes.Estados.VISITA_PRESENCIAL;
            }

            // test se rellenan los datos y el CIF sobre todo
            clienteDB.CIF_NIF   = clienteModificar.Nif;
            clienteDB.Nombre    = clienteModificar.Nombre;
            clienteDB.Dirección = clienteModificar.Direccion;
            // Aquí hay que modificar la población y la provincia si el código postal ha cambiado
            if (clienteDB.CodPostal?.Trim() != clienteModificar.CodigoPostal)
            {
                CodigoPostal cp = await servicio.BuscarCodigoPostal(clienteModificar.Empresa, clienteModificar.CodigoPostal).ConfigureAwait(false);
                clienteDB.Población = cp.Descripción?.Substring(0, 30);
                clienteDB.Provincia = cp.Provincia;
            }
            clienteDB.CodPostal = clienteModificar.CodigoPostal;
            clienteDB.Teléfono  = clienteModificar.Telefono;
            clienteDB.Vendedor  = clienteModificar.VendedorEstetica;
            clienteDB.Comentarios = clienteModificar.Comentarios;
            clienteDB.ComentarioPicking = clienteModificar.ComentariosPicking;
            clienteDB.ComentarioRuta = clienteModificar.ComentariosRuta;

            if (clienteDB.CondPagoClientes != null && clienteDB.CondPagoClientes.Count > 0 && (
                clienteDB.CondPagoClientes.OrderBy(c => c.ImporteMínimo).First().PlazosPago.Trim() != clienteModificar.PlazosPago.Trim() ||
                clienteDB.CondPagoClientes.OrderBy(c => c.ImporteMínimo).First().FormaPago.Trim() != clienteModificar.FormaPago.Trim()))
            {
                CondPagoCliente condPagoNueva = new CondPagoCliente
                {
                    Empresa = clienteModificar.Empresa,
                    Nº_Cliente = clienteModificar.Cliente,
                    Contacto = clienteModificar.Contacto,
                    PlazosPago = clienteModificar.PlazosPago,
                    FormaPago = clienteModificar.FormaPago,
                    ImporteMínimo = 0
                };
                CondPagoCliente condPagoActual = clienteDB.CondPagoClientes.OrderBy(c => c.ImporteMínimo).First();
                clienteDB.CondPagoClientes.Remove(condPagoActual);
                clienteDB.CondPagoClientes.Add(condPagoNueva);
            }

            
            if (clienteModificar.FormaPago == Constantes.FormasPago.RECIBO_BANCARIO && !string.IsNullOrWhiteSpace(clienteModificar.Iban))
            {
                Iban iban = new Iban(clienteModificar.Iban);
                bool esElMismoIban = false;
                //CCC cccEncontrado = await servicio.BuscarIban(clienteModificar.Empresa, clienteModificar.Cliente, iban).ConfigureAwait(false);
                if (clienteDB.CCC1 != null && (
                clienteDB.CCC1.Pais == iban.Pais &&
                clienteDB.CCC1.DC_IBAN == iban.DigitoControlPais &&
                clienteDB.CCC1.Entidad == iban.Entidad &&
                clienteDB.CCC1.Oficina == iban.Oficina &&
                clienteDB.CCC1.DC == iban.DigitoControl &&
                clienteDB.CCC1.Nº_Cuenta == iban.NumeroCuenta))
                {
                    // TODO: permitir modificar IBAN, pero ponerlo en estado "en poder del vendedor"
                    //throw new Exception("El IBAN no se puede modificar. Debe hacerlo administración cuando tenga el mandato firmado en su poder.");
                    esElMismoIban = true;
                }

                // TODO: hacer test que si añado iban a uno que no tiene, me deja
                if (!esElMismoIban && !string.IsNullOrWhiteSpace(clienteModificar.Iban))
                {
                    // 1. mirar si existe pero no está puesto en ficha y ponerlo
                    CCC cccEncontrado = await servicio.BuscarIban(db, clienteModificar.Empresa, clienteModificar.Cliente, clienteModificar.Contacto, iban).ConfigureAwait(false);
                    if (cccEncontrado == null)
                    {
                        cccEncontrado = await servicio.BuscarIban(db, clienteModificar.Empresa, clienteModificar.Cliente, iban).ConfigureAwait(false);
                    }
                    if (cccEncontrado != null)
                    {
                        if (cccEncontrado.Contacto?.Trim() == clienteModificar.Contacto?.Trim()) 
                        {
                            clienteDB.CCC = cccEncontrado.Número;
                            if (cccEncontrado.Estado == Constantes.Clientes.Estados.NULO)
                            {
                                bool recuperado = await servicio.RecuperarCCC(cccEncontrado);
                                if (!recuperado)
                                {
                                    throw new Exception("No se pudo recuperar el CCC");
                                }
                            }
                        }
                        else
                        {
                            // crear el CCC en el contacto
                            CCC nuevoCCC = new CCC
                            {
                                //Cliente1 = clienteDB, 
                                Empresa = clienteDB.Empresa,
                                Cliente = clienteDB.Nº_Cliente,
                                Contacto = clienteDB.Contacto,
                                Número = cccEncontrado.Número,
                                Pais = iban.Pais,
                                DC_IBAN = iban.DigitoControlPais,
                                Entidad = iban.Entidad,
                                Oficina = iban.Oficina,
                                DC = iban.DigitoControl,
                                Nº_Cuenta = iban.NumeroCuenta,
                                Estado = cccEncontrado.Estado,
                                Secuencia = cccEncontrado.Secuencia,
                                Usuario = clienteModificar.Usuario
                            };

                            /*
                            //db.Entry(nuevoCCC).State = EntityState.Added;
                            //clienteDB.CCC1 = nuevoCCC;
                            nuevoCCC.Clientes.Add(clienteDB);
                            db.Entry(nuevoCCC).State = EntityState.Detached;
                            //clienteDB.CCCs.Add(nuevoCCC);
                            clienteDB.CCC = nuevoCCC.Número;
                            //clienteDB.CCC = cccEncontrado.Número;
                            */
                            bool creado = await servicio.CrearCCC(nuevoCCC).ConfigureAwait(false);
                            if (!creado)
                            {
                                throw new Exception("No se pudo crear el CCC");
                            }
                            clienteDB.CCC = nuevoCCC.Número;
                        }
                    }
                    else
                    {
                        // 2. cc. crearlo -> Comentar y hacer test
                        CCC nuevoCCC = new CCC
                        {
                            //Cliente1 = clienteDB, 
                            Empresa = clienteDB.Empresa,
                            Cliente = clienteDB.Nº_Cliente,
                            Contacto = clienteDB.Contacto,
                            Número = (await servicio.MayorCCC(clienteModificar.Empresa, clienteModificar.Cliente).ConfigureAwait(false) + 1).ToString(),
                            Pais = iban.Pais,
                            DC_IBAN = iban.DigitoControlPais,
                            Entidad = iban.Entidad,
                            Oficina = iban.Oficina,
                            DC = iban.DigitoControl,
                            Nº_Cuenta = iban.NumeroCuenta,
                            Estado = Constantes.Clientes.EstadosMandatos.EN_PODER_DEL_CLIENTE,
                            Secuencia = Constantes.Clientes.SECUENCIA_POR_DEFECTO,
                            Usuario = clienteModificar.Usuario
                        };

                        bool creado = await servicio.CrearCCC(nuevoCCC).ConfigureAwait(false);
                        if (!creado)
                        {
                            throw new Exception("No se pudo crear el CCC");
                        }
                        clienteDB.CCC = nuevoCCC.Número;
                    }                    
                }
            }

            
            
            for (var i = 0; i < clienteDB.PersonasContactoClientes.Count; i++)
            {
                PersonaContactoCliente personaExistente = clienteDB.PersonasContactoClientes.ElementAt(i);
                PersonaContactoDTO personaEncontrada = clienteModificar.PersonasContacto.SingleOrDefault(p => p.Numero.ToString() == personaExistente.Número.Trim());
                if (personaEncontrada == null)
                {
                    clienteDB.PersonasContactoClientes.Remove(personaExistente);
                }
            }

            // personas de contacto
            foreach (var persona in clienteModificar.PersonasContacto)
            {
                bool encontrada = persona.Numero > 0;
                PersonaContactoCliente personaEncontrada = null;
                if (encontrada)
                {
                    personaEncontrada = clienteDB.PersonasContactoClientes.FirstOrDefault(p => p.Número.Trim() == persona.Numero.ToString());
                    encontrada = personaEncontrada != null;
                }
                
                if (!encontrada)
                {
                    int ultimoNumero = 0;
                    try
                    {
                        ultimoNumero = Int32.Parse(clienteDB.PersonasContactoClientes.Max(p => p.Número));
                    }
                    catch
                    {
                        ultimoNumero = 1;
                    }
                    PersonaContactoCliente personaContacto = new PersonaContactoCliente
                    {
                        Empresa = clienteDB.Empresa,
                        Cliente = clienteDB,
                        Número = (ultimoNumero+1).ToString(),
                        Cargo = persona.FacturacionElectronica ? Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO : Constantes.Clientes.CARGO_POR_DEFECTO,
                        Nombre = persona.Nombre,
                        CorreoElectrónico = persona.CorreoElectronico,
                        EnviarBoletin = true,
                        Estado = 0,
                        Usuario = clienteModificar.Usuario
                    };
                    clienteDB.PersonasContactoClientes.Add(personaContacto);
                } else
                {
                    if (persona.Nombre?.Trim() != personaEncontrada.Nombre?.Trim())
                    {
                        personaEncontrada.Nombre = persona.Nombre;
                    }
                    if (persona.CorreoElectronico?.Trim() != personaEncontrada.CorreoElectrónico?.Trim())
                    {
                        personaEncontrada.CorreoElectrónico = persona.CorreoElectronico;
                    }
                    if (persona.FacturacionElectronica && personaEncontrada.Cargo != Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
                    {
                        personaEncontrada.Cargo = Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO;
                    }
                    if (!persona.FacturacionElectronica && personaEncontrada.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
                    {
                        personaEncontrada.Cargo = Constantes.Clientes.CARGO_POR_DEFECTO;
                    }
                }
            }

            return clienteDB;
        }

        public async Task<Cliente> PrepararClienteCrear(ClienteCrear clienteCrear, NVEntities db)
        {
            string contacto = await servicio.CalcularSiguienteContacto(clienteCrear.Empresa, clienteCrear.Cliente);

            if (clienteCrear.Nif != null && clienteCrear.Nif.Trim() == String.Empty)
            {
                clienteCrear.Nif = null;
            }

            var vendedoresTelefono = await servicio.VendedoresTelefonicos().ConfigureAwait(false);
            if (clienteCrear.Estado == Constantes.Clientes.Estados.VISITA_PRESENCIAL && vendedoresTelefono.Contains(clienteCrear.VendedorEstetica))
            {
                clienteCrear.Estado = Constantes.Clientes.Estados.VISITA_TELEFONICA;
            }

            Cliente cliente = new Cliente
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Nº_Cliente = clienteCrear.Cliente,
                Contacto = contacto,

                AlbaranValorado = true,
                CIF_NIF = clienteCrear.Nif,
                ClientePrincipal = !clienteCrear.EsContacto,
                CodPostal = clienteCrear.CodigoPostal,
                Comentarios = clienteCrear.Comentarios,
                ComentarioPicking = clienteCrear.ComentariosPicking,
                ComentarioRuta = clienteCrear.ComentariosRuta,
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
                Vendedor = clienteCrear.VendedorEstetica,
                Usuario = clienteCrear.Usuario
            };

            if (clienteCrear.Peluqueria && !clienteCrear.Estetica)
            {
                clienteCrear.VendedorPeluqueria = clienteCrear.VendedorPeluqueria ?? cliente.Vendedor;
                clienteCrear.VendedorEstetica = Constantes.Vendedores.VENDEDOR_GENERAL;
                cliente.Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL;
            }

            if (clienteCrear.Peluqueria && clienteCrear.VendedorPeluqueria != null && clienteCrear.VendedorPeluqueria != clienteCrear.VendedorEstetica)
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
            foreach (PersonaContactoDTO personaCrear in clienteCrear.PersonasContacto.Where(p => !string.IsNullOrEmpty(p.Nombre) || !string.IsNullOrEmpty(p.CorreoElectronico)))
            {
                PersonaContactoCliente persona = new PersonaContactoCliente
                {
                    Empresa = cliente.Empresa,
                    Cliente = cliente,
                    Número = i++.ToString(),
                    Cargo = personaCrear.FacturacionElectronica ? Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO : Constantes.Clientes.CARGO_POR_DEFECTO,
                    Nombre = personaCrear.Nombre,
                    CorreoElectrónico = personaCrear.CorreoElectronico,
                    EnviarBoletin = true,
                    Estado = 0,
                    Saludo = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(personaCrear.Nombre?.ToLower()),
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

            if (clienteCrear.FormaPago == Constantes.FormasPago.RECIBO_BANCARIO && clienteCrear.Iban != null && !string.IsNullOrEmpty(clienteCrear.Iban))
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


        public ByteArrayContent MandatoEnPDF(List<Mandato> mandatos)
        {
            Warning[] warnings;
            string mimeType;
            string[] streamids;
            string encoding;
            string filenameExtension;

            ReportViewer viewer = new ReportViewer();
            viewer.LocalReport.ReportPath = @"Models\Clientes\MandatoCoreSEPA.rdlc";
            viewer.LocalReport.DataSources.Add(new ReportDataSource("MandatoDataSet", mandatos));
            viewer.LocalReport.EnableExternalImages = true;

            viewer.LocalReport.Refresh();

            var bytes = viewer.LocalReport.Render("PDF", null, out mimeType, out encoding, out filenameExtension, out streamids, out warnings);

            viewer.LocalReport.Dispose();
            viewer.Dispose();

            return new ByteArrayContent(bytes);
        }

        public async Task<Mandato> LeerMandato(string empresa, string cliente, string contacto, string ccc)
        {
            Cliente clienteDB = await servicio.BuscarCliente(empresa, cliente, contacto).ConfigureAwait(false);
            CCC cccDB = await servicio.BuscarCCC(empresa, cliente, contacto, ccc).ConfigureAwait(false);

            Mandato mandato = new Mandato
            {
                Referencia = $"{empresa}/{cliente}/{ccc}",
                IdentificadorAcreedor = "ESA78368255",
                NombreAcreedor = "Nueva Visión, S.A.",
                DireccionAcreedor = "C/ Río Tiétar, 11, nave",
                CodigoPostalAcreedor = "28119",
                PoblacionAcreedor = "Algete",
                ProvinciaAcreedor = "Madrid",
                PaisAcreedor = "España",
                NombreDeudor = clienteDB.Nombre.Trim(),
                DireccionDeudor = clienteDB.Dirección.Trim(),
                CodigoPostalDeudor = clienteDB.CodPostal.Trim(),
                PoblacionDeudor = clienteDB.Población.Trim(),
                ProvinciaDeudor = clienteDB.Provincia.Trim(),
                PaisDeudor = "España",
                Iban = new Iban($"{cccDB.Pais}{cccDB.DC_IBAN}{cccDB.Entidad}{cccDB.Oficina}{cccDB.DC}{cccDB.Nº_Cuenta}"),
                SwiftBic = cccDB.BIC,
                PersonaFirmante = "0123456789XY".Contains(clienteDB.CIF_NIF.Substring(0, 1)) ? clienteDB.Nombre : String.Empty
            };

            return mandato;
        }

        public async Task<List<ClienteProbabilidadVenta>> BuscarClientesPorProbabilidadVenta(string vendedor, int numeroClientes, string tipoInteraccion, string grupoSubgrupo = "")
        {
            var mlContext = new MLContext();

            ITransformer model;
            using (var fileStream = new FileStream(AppDomain.CurrentDomain.BaseDirectory + "\\ModelsIA\\modelo_llamadas.zip", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                model = mlContext.Model.Load(fileStream, out var modelInputSchema);
            }

            List<ClienteInteraccion> clientes = ObtenerClientes(vendedor, tipoInteraccion);

            if (!string.IsNullOrEmpty(grupoSubgrupo))
            {
                foreach (var cliente in clientes)
                {
                    cliente.GrupoSubgrupoMasVendido = grupoSubgrupo;
                }
            }

            // Convertir clientes a IDataView para aplicar el modelo de ML
            IDataView clientesData = mlContext.Data.LoadFromEnumerable(clientes);

            // Predecir usando el modelo de ML
            var predictions = model.Transform(clientesData);

            // Extraer resultados de predicción
            var predictionResults = mlContext.Data.CreateEnumerable<PrediccionModelo>(predictions, reuseRowObject: false);

            // Seleccionar mejores clientes basados en la probabilidad
            var mejoresClientes = predictionResults
                .Select((pred, index) => new { Cliente = clientes[index], Probabilidad = pred.Probability })
                .OrderByDescending(c => c.Probabilidad)
                .Take(numeroClientes)
                .ToList();

            // Preparar lista de clientes con probabilidad de venta
            List<ClienteProbabilidadVenta> clientesProbabilidadVenta = new List<ClienteProbabilidadVenta>();

            // Recorrer cada cliente y obtener datos adicionales de la base de datos
            foreach (var item in mejoresClientes)
            {
                // Dividir el ClienteId para obtener Cliente y Contacto
                string[] clienteParts = item.Cliente.ClienteId.Split('/');
                string clienteId = clienteParts[0];   // Cliente
                string contacto = clienteParts.Length > 1 ? clienteParts[1] : string.Empty; // Contacto

                // Obtener datos del cliente desde la base de datos (similar al método GetCliente)
                var clienteEncontrado = await servicio.BuscarCliente(Constantes.Empresas.EMPRESA_POR_DEFECTO, clienteId, contacto);

                if (clienteEncontrado != null)
                {
                    // Crear un ClienteProbabilidadVenta y asignar la probabilidad
                    ClienteProbabilidadVenta clienteProbabilidad = new ClienteProbabilidadVenta
                    {
                        albaranValorado = clienteEncontrado.AlbaranValorado,
                        cadena = clienteEncontrado.Cadena?.Trim(),
                        ccc = clienteEncontrado.CCC?.Trim(),
                        cifNif = clienteEncontrado.CIF_NIF?.Trim(),
                        cliente = clienteEncontrado.Nº_Cliente.Trim(),
                        clientePrincipal = clienteEncontrado.ClientePrincipal,
                        codigoPostal = clienteEncontrado.CodPostal?.Trim(),
                        comentarioPicking = clienteEncontrado.ComentarioPicking,
                        comentarioRuta = clienteEncontrado.ComentarioRuta,
                        comentarios = clienteEncontrado.Comentarios,
                        contacto = clienteEncontrado.Contacto.Trim(),
                        copiasAlbaran = clienteEncontrado.NºCopiasAlbarán,
                        copiasFactura = clienteEncontrado.NºCopiasFactura,
                        direccion = clienteEncontrado.Dirección?.Trim(),
                        empresa = clienteEncontrado.Empresa.Trim(),
                        estado = clienteEncontrado.Estado,
                        grupo = clienteEncontrado.Grupo,
                        iva = clienteEncontrado.IVA,
                        mantenerJunto = clienteEncontrado.MantenerJunto,
                        noComisiona = clienteEncontrado.NoComisiona,
                        nombre = clienteEncontrado.Nombre?.Trim(),
                        periodoFacturacion = clienteEncontrado.PeriodoFacturación,
                        poblacion = clienteEncontrado.Población?.Trim(),
                        provincia = clienteEncontrado.Provincia?.Trim(),
                        ruta = clienteEncontrado.Ruta,
                        servirJunto = clienteEncontrado.ServirJunto,
                        telefono = clienteEncontrado.Teléfono,
                        vendedor = clienteEncontrado.Vendedor,
                        web = clienteEncontrado.Web,                        
                        Probabilidad = item.Probabilidad,
                        DiasDesdeUltimaInteraccion = (int)item.Cliente.DiasDesdeUltimaInteraccion,
                        DiasDesdeUltimoPedido = (int)item.Cliente.DiasDesdeUltimoPedido
                    };

                    // Agregar el cliente a la lista final
                    clientesProbabilidadVenta.Add(clienteProbabilidad);
                }
            }

            return clientesProbabilidadVenta;
        }



        static List<ClienteInteraccion> ObtenerClientes(string vendedor, string tipoInteraccion)
        {
            if (string.IsNullOrEmpty(tipoInteraccion) || tipoInteraccion == "Teléfono")
            {
                tipoInteraccion = "Llamada";
            }
            string query = $@"
            select row_number() over (partition by l.[nº cliente], l.contacto order by sum(cantidad) desc) rn, L.[Nº Cliente] Cliente, L.Contacto, L.Grupo + Subgrupo GrupoSubgrupo, sum(Cantidad) Ventas
            into #Gruposubgrupo
            from LinPedidoVta l inner join Clientes c
            on l.[Nº Cliente] = c.[Nº Cliente] and l.Contacto = c.Contacto
            where c.Vendedor = '{vendedor}' AND [Fecha Factura] >= DATEADD(year, -5, getdate()) and SubGrupo != 'MMP' and L.Estado = 4
            group by l.[Nº Cliente], l.Contacto, DATEPART(month, [Fecha Factura]), DATEPART(YEAR, [Fecha Factura]), l.Grupo + Subgrupo;

            WITH cte_fechas AS (
                SELECT 
                    GETDATE() AS FechaHoy, 
                    DATEADD(month, -11, GETDATE()) AS FechaHace11Meses,
                    DATEADD(yy, -1, GETDATE()) AS FechaHace12Meses, 
                    DATEADD(yy, -5, GETDATE()) AS FechaHace5Años
            ),
            cte_clientes AS (
                SELECT 
                    TRIM([nº cliente]) + '/' + TRIM(contacto) AS ClienteId, 
		            [nº Cliente] Cliente,
		            Contacto,
                    Nombre AS NombreCliente
                FROM 
                    Clientes c
                WHERE 
                    empresa = '1' 
                    AND estado >= 0 
                    AND Estado not in (7, 67)
                    AND vendedor = '{vendedor}'
            ),
            cte_interacciones AS (
                SELECT 
                    Número Cliente,
		            Contacto,
                    DATEADD(HOUR, -1, Fecha) AS FechaInteraccion, 
                    CASE Tipo 
                        WHEN 'T' THEN 'Llamada' 
                        WHEN 'V' THEN 'Visita' 
                        WHEN 'W' THEN 'WhatsApp' 
                    END AS TipoInteraccion
                FROM 
                    SeguimientoCliente
                WHERE 
                    Fecha >= (SELECT FechaHace5Años FROM cte_fechas) 
                    AND Estado = 0 AND Pedido = 0
            ),
            cte_pedidos AS (
                SELECT 
		            c.[Nº Cliente] Cliente,
		            c.Contacto,
                    c.Número AS PedidoId, 
                    CASE 
                        WHEN c.[Periodo Facturacion] = 'FDM' THEN l.[Fecha Modificacion] 
                        ELSE c.[Fecha Modificación] 
                    END AS FechaPedido
                FROM 
                    CabPedidoVta c 
                INNER JOIN 
                    LinPedidoVta l ON c.empresa = l.empresa AND c.número = l.número
                WHERE 
                    TipoLinea = 1 and Estado >= -1 AND c.NotaEntrega = 0
                    AND [base imponible] > 0 
                    AND c.Fecha >= (SELECT FechaHace5Años FROM cte_fechas)
            )
            SELECT 
                c.ClienteId,
                '{tipoInteraccion}' AS TipoInteraccion, 
                DATEPART(MONTH, (SELECT FechaHoy FROM cte_fechas)) AS MesActual,
                DATEPART(WEEKDAY, (SELECT FechaHoy FROM cte_fechas)) AS DiaDeLaSemana,
                CASE WHEN DATEPART(HOUR, (SELECT FechaHoy FROM cte_fechas)) > 14 THEN 1 ELSE 0 END AS EsPorLaTarde,
                ISNULL(DATEDIFF(DAY, (SELECT MAX(FechaInteraccion) FROM cte_interacciones i WHERE i.Cliente = c.Cliente and i.Contacto = c.Contacto), (SELECT FechaHoy FROM cte_fechas)), 9999) AS DiasDesdeUltimaInteraccion,
                ISNULL(DATEDIFF(DAY, (SELECT MAX(FechaPedido) FROM cte_pedidos p WHERE p.Cliente = c.Cliente and p.Contacto = c.Contacto), (SELECT FechaHoy FROM cte_fechas)), 9999) AS DiasDesdeUltimoPedido,
                isnull((SELECT ISNULL(365 / NULLIF(COUNT(DISTINCT CAST(FechaPedido AS date)), 0), 0) FROM cte_pedidos p WHERE p.Cliente = c.Cliente and p.Contacto = c.Contacto AND p.FechaPedido >= (SELECT FechaHace12Meses FROM cte_fechas)), 0) AS FrecuenciaPedidosUltimoAnno,
                ISNULL((SELECT COUNT(*) FROM cte_interacciones p WHERE p.Cliente = c.Cliente and p.Contacto = c.Contacto AND p.FechaInteraccion >= (SELECT FechaHace12Meses FROM cte_fechas)), 0) AS InteraccionesUltimos12Meses,
                isnull((SELECT COUNT(distinct(cast(FechaPedido as DATE))) FROM cte_pedidos p WHERE p.Cliente = c.Cliente and p.Contacto = c.Contacto AND p.FechaPedido >= (SELECT FechaHace12Meses FROM cte_fechas) AND p.FechaPedido < (SELECT FechaHace11Meses FROM cte_fechas)), 0) AS PedidosMesAnnoAnterior,
                isnull((select GrupoSubgrupo from #grupoSubgrupo g where rn = 1 and g.Cliente = c.Cliente and g.Contacto= c.Contacto), 'NADA') GrupoSubgrupoMasVendido,
                0 AS target
            FROM 
                cte_clientes c
            ORDER BY 
                c.ClienteId;
            ";

            using (var context = new NVEntities()) 
            {
                var connectionString = context.Database.Connection.ConnectionString;

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    SqlCommand command = new SqlCommand(query, connection);
                    connection.Open();
                    SqlDataReader reader = command.ExecuteReader();

                    List<ClienteInteraccion> clientes = new List<ClienteInteraccion>();

                    while (reader.Read())
                    {
                        try
                        {
                            clientes.Add(new ClienteInteraccion
                            {
                                ClienteId = reader["ClienteId"].ToString(),
                                TipoInteraccion = reader["TipoInteraccion"].ToString(),
                                MesActual = reader["MesActual"].ToString(),
                                DiaDeLaSemana = reader["DiaDeLaSemana"].ToString(),
                                GrupoSubgrupoMasVendido = reader["GrupoSubgrupoMasVendido"].ToString(),
                                EsPorLaTarde = Convert.ToSingle(reader["EsPorLaTarde"]),
                                DiasDesdeUltimaInteraccion = Convert.ToSingle(reader["DiasDesdeUltimaInteraccion"]),
                                DiasDesdeUltimoPedido = Convert.ToSingle(reader["DiasDesdeUltimoPedido"]),
                                FrecuenciaPedidosUltimoAnno = Convert.ToSingle(reader["FrecuenciaPedidosUltimoAnno"]),
                                InteraccionesUltimos12Meses = Convert.ToSingle(reader["InteraccionesUltimos12Meses"]),
                                PedidosMesAnnoAnterior = Convert.ToSingle(reader["PedidosMesAnnoAnterior"]),
                                Target = Convert.ToBoolean(reader["target"])
                            });
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("No se pudo leer los datos del modele", ex);
                        }
                        
                    }

                    var clientesFiltrados = clientes.Where(c => c.DiasDesdeUltimaInteraccion >= 7 && c.DiasDesdeUltimoPedido >= 6).ToList();

                    return clientesFiltrados;
                }
            }           
        }

        public async Task<Cliente> ModificarCliente(ClienteCrear clienteCrear, NVEntities db)
        {
            Cliente cliente = new Cliente();
            try
            {
                cliente = await PrepararClienteModificar(clienteCrear, db);
                db.Entry(cliente).State = EntityState.Modified;

                await db.SaveChangesAsync();
                if (cliente.CCCs.Count != 0 && cliente.CCC == null)
                {
                    cliente.CCC1 = cliente.CCCs.FirstOrDefault();
                    await db.SaveChangesAsync();
                }

                // Publicar evento de sincronización
                await PublicarClienteSincronizar(cliente);

                return cliente;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ClienteExists(db, cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto))
                {
                    throw new NotFoundException();
                }
                throw;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<Cliente> CrearCliente(ClienteCrear clienteCrear, NVEntities db)
        {
            Cliente cliente = new Cliente();
            try
            {
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

                cliente = await PrepararClienteCrear(clienteCrear, db);
                db.Clientes.Add(cliente);

                await db.SaveChangesAsync();
                if (cliente.CCCs.Count != 0 && cliente.CCC == null)
                {
                    cliente.CCC1 = cliente.CCCs.FirstOrDefault();
                    await db.SaveChangesAsync();
                }

                await PublicarClienteSincronizar(cliente);

                return cliente;
            }
            catch (DbUpdateException)
            {
                if (ClienteExists(db, cliente.Empresa, cliente.Nº_Cliente, cliente.Contacto))
                {
                    throw new ConflictException();
                }
                throw;
            }
            catch (DbEntityValidationException ex)
            {
                var error = ex.EntityValidationErrors
                    .FirstOrDefault()?.ValidationErrors
                    .FirstOrDefault();

                if (error != null)
                {
                    throw new ValidationException(error.ErrorMessage);
                }
                throw;
            }
        }

        public async Task PublicarClienteSincronizar(Cliente cliente)
        {
            var personasContacto = cliente.PersonasContactoClientes
                .Where(p => p.Empresa.Trim() == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Estado >= 0)
                .Select(p => new { Id = p.Número?.Trim(), Nombre = p.Nombre?.Trim(), CorreoElectronico = p.CorreoElectrónico?.Trim(), Telefonos = p.Teléfono?.Trim(), Cargo = p.Cargo }) 
                .ToList(); // Convertir la lista a un tipo que se pueda serializar

            // Publicar evento de sincronización
            var message = new
            {
                Nif = cliente.CIF_NIF?.Trim(),
                Cliente = cliente.Nº_Cliente?.Trim(),
                Contacto = cliente.Contacto?.Trim(),
                ClientePrincipal = cliente.ClientePrincipal,
                Nombre = cliente.Nombre?.Trim(),
                Direccion = cliente.Dirección?.Trim(),
                CodigoPostal = cliente.CodPostal?.Trim(),
                Poblacion = cliente.Población?.Trim(),
                Provincia = cliente.Provincia?.Trim(),
                Telefono = cliente.Teléfono?.Trim(),
                Comentarios = cliente.Comentarios?.Trim(),
                Vendedor = cliente.Vendedor?.Trim(),
                Estado = cliente.Estado,
                PersonasContacto = personasContacto,
                Tabla = "Clientes",
                Source = "Nesto"
            };

            string jsonMessage = JsonConvert.SerializeObject(message);

            await _sincronizacionEventWrapper.PublishSincronizacionEventAsync("sincronizacion-tablas", jsonMessage);
        }

        private bool ClienteExists(NVEntities db, string empresa, string numCliente, string contacto)
        {
            return db.Clientes.Count(e => e.Empresa == empresa && e.Nº_Cliente == numCliente && e.Contacto == contacto) > 0;
        }

        public async Task<ClienteDTO> BuscarClientePorEmailNif(string email, string nif)
        {
            return await servicio.BuscarClientePorEmailNif(email, nif);
        }
    }
}

public class NotFoundException : Exception { }
public class ConflictException : Exception { }