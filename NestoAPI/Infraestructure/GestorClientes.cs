using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using static NestoAPI.Models.Clientes.RespuestaDatosGeneralesClientes;

using System.Data.Entity;


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
            var direccionFormateada = "";
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
                    numeroCalleFormateada = direccion.IndexOf("S/N", StringComparison.InvariantCulture) != -1 ? "S/N" : "";
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
                if (direccionFormateada != "")
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
                    CorreoElectronico = persona.CorreoElectrónico?.Trim()
                });
            }

            return clienteCrear;
        }
                
        public async Task<List<Cliente>> DejarDeVisitar(NVEntities db, ClienteCrear cliente)
        {
            // En el parámetro "cliente" es donde marcamos el cambio:
            // - Si vendedorEstetica es NV es el vendedor de estética el que se lo quita
            // - Si vendedorPeluqueria es NV es el vendedor de peluquería el que se lo quita
            // No hay riesgo de que el cliente ya fuese de NV, porque solo marcamos el vendedor que se lo quita, no el que tiene actualmente

            List<Cliente> clientesModificados = new List<Cliente>();

            Cliente clienteDB = await servicio.BuscarCliente(db, cliente.Empresa, cliente.Cliente, cliente.Contacto);

            var vendedoresTelefono = await servicio.VendedoresTelefonicos();
            
            int diaVendedor = servicio.Hoy().Minute % vendedoresTelefono.Count;
            string nuevoVendedor = vendedoresTelefono.Contains(clienteDB.Vendedor) ? 
                clienteDB.Vendedor :
                vendedoresTelefono.ElementAt(diaVendedor);

            List<Cliente> contactos = await servicio.BuscarContactos(db, cliente.Empresa, cliente.Cliente, cliente.Contacto);
            List<string> vendedoresContacto = VendedoresContactosCliente(contactos);
            if (nuevoVendedor == vendedoresTelefono.ElementAt(diaVendedor))
            {
                var vendedoresContactoTelefonicos = vendedoresContacto.Intersect(vendedoresTelefono);
                if (vendedoresContactoTelefonicos.FirstOrDefault() != null)
                {
                    nuevoVendedor = vendedoresContactoTelefonicos.First();
                }
            }
            
            string vendedorPeluqueria = string.Empty;
            List<string> vendedoresPresenciales = await servicio.VendedoresPresenciales();


            // Se lo quita un comercial de estética
            if (cliente.VendedorEstetica == Constantes.Vendedores.VENDEDOR_GENERAL && clienteDB.Vendedor != Constantes.Vendedores.VENDEDOR_GENERAL)
            {
                clienteDB.Vendedor = nuevoVendedor;
                clienteDB.Estado = Constantes.Clientes.Estados.VISITA_TELEFONICA;
                if (clienteDB.VendedoresClienteGrupoProductoes == null ||
                    clienteDB.VendedoresClienteGrupoProductoes
                        .SingleOrDefault(v => v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA)?
                        .Vendedor?.Trim() == Constantes.Vendedores.VENDEDOR_GENERAL)
                {
                    vendedorPeluqueria = nuevoVendedor;
                }

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
            clienteDB.CodPostal = clienteModificar.CodigoPostal;
            clienteDB.Teléfono  = clienteModificar.Telefono;
            clienteDB.Vendedor  = clienteModificar.VendedorEstetica;
            
            if (clienteDB.CondPagoClientes != null && clienteDB.CondPagoClientes.Count > 0 && (
                clienteDB.CondPagoClientes.First().PlazosPago.Trim() != clienteModificar.PlazosPago.Trim() ||
                clienteDB.CondPagoClientes.First().FormaPago.Trim() != clienteModificar.FormaPago.Trim()))
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
                CondPagoCliente condPagoActual = clienteDB.CondPagoClientes.First();
                clienteDB.CondPagoClientes.Remove(condPagoActual);
                clienteDB.CondPagoClientes.Add(condPagoNueva);
            }

            if (clienteModificar.FormaPago == Constantes.FormasPago.RECIBO_BANCARIO &&
                clienteModificar.Iban != null && clienteModificar.Iban?.Trim() != "" && clienteDB.CCC1 != null && (
                clienteDB.CCC1.Pais != clienteModificar.Iban.Substring(0, 2) ||
                clienteDB.CCC1.DC_IBAN != clienteModificar.Iban.Substring(2, 2) ||
                clienteDB.CCC1.Entidad != clienteModificar.Iban.Substring(4, 4) ||
                clienteDB.CCC1.Oficina != clienteModificar.Iban.Substring(8, 4) ||
                clienteDB.CCC1.DC != clienteModificar.Iban.Substring(12, 2) ||
                clienteDB.CCC1.Nº_Cuenta != clienteModificar.Iban.Substring(14, 10)))
            {
                throw new Exception("El IBAN no se puede modificar. Debe hacerlo administración cuando tenga el mandato firmado en su poder.");
            }

            // TODO: hacer test que si añado iban a uno que no tiene, me deja
            if (clienteModificar.Iban?.Trim() != "" && clienteDB.CCC1 == null)
            {
                CCC nuevoCCC = new CCC
                {
                    Cliente1 = clienteDB,
                    Número = "1", //TODO: mirar cual toca
                    Pais = clienteModificar.Iban.Substring(0, 2),
                    DC_IBAN = clienteModificar.Iban.Substring(2, 2),
                    Entidad = clienteModificar.Iban.Substring(5, 4),
                    Oficina = clienteModificar.Iban.Substring(10, 4),
                    DC = clienteModificar.Iban.Substring(15, 2),
                    Nº_Cuenta = clienteModificar.Iban.Substring(17, 2)
                    + clienteModificar.Iban.Substring(20, 4)
                    + clienteModificar.Iban.Substring(25, 4),
                    Estado = Constantes.Clientes.EstadosMandatos.EN_PODER_DEL_CLIENTE,
                    Secuencia = Constantes.Clientes.SECUENCIA_POR_DEFECTO,
                    Usuario = clienteModificar.Usuario
                };

                clienteDB.CCCs.Add(nuevoCCC);
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

            if (clienteCrear.FormaPago == Constantes.FormasPago.RECIBO_BANCARIO && clienteCrear.Iban != null && clienteCrear.Iban != "")
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
    }
}