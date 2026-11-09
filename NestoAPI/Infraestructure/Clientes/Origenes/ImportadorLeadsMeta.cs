using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Origenes
{
    /// <summary>NestoAPI#623 corte 4: qué pasa (o pasaría, en ensayo) con un lead del fichero.</summary>
    public class ResultadoLeadMetaDTO
    {
        public int Fila { get; set; }
        public string Lead { get; set; }
        public DateTime? FechaLead { get; set; }
        public string Nombre { get; set; }
        public string Telefono { get; set; }
        public string Email { get; set; }
        public string CodigoPostal { get; set; }
        public bool AceptaComunicaciones { get; set; }
        /// <summary>CREAR (en ensayo) / CREADO / YA_EXISTIA / ERROR.</summary>
        public string Codigo { get; set; }
        /// <summary>Para la rejilla: «Se creará», «Creado», «Ya existía», «Error».</summary>
        public string Accion { get; set; }
        public string Motivo { get; set; }
        /// <summary>El cliente creado o el que ya existía (null si no hay).</summary>
        public string Cliente { get; set; }
        /// <summary>El vendedor de la zona (por el código postal) o el de la ficha que ya existía.</summary>
        public string Vendedor { get; set; }
    }

    /// <summary>NestoAPI#623 corte 4: respuesta de POST api/Clientes/Origenes/ImportarLeads.</summary>
    public class ResultadoImportacionLeadsDTO
    {
        public bool Ensayo { get; set; }
        public string Origen { get; set; }
        public int Leidos { get; set; }
        /// <summary>Creados (o que se crearían, en ensayo).</summary>
        public int Creados { get; set; }
        public int YaExistian { get; set; }
        public int Errores { get; set; }
        public List<ResultadoLeadMetaDTO> Leads { get; set; } = new List<ResultadoLeadMetaDTO>();
    }

    /// <summary>Un cliente que ya tiene el teléfono o el correo del lead.</summary>
    public class ClienteParecidoLead
    {
        public string Cliente { get; set; }
        public string Origen { get; set; }
        public string Vendedor { get; set; }
    }

    public enum ResultadoOrigenLead
    {
        /// <summary>Tenía otro origen: no se toca.</summary>
        OtroOrigen = 0,
        /// <summary>No tenía origen: se le ha puesto.</summary>
        Puesto = 1,
        /// <summary>Ya tenía este mismo origen.</summary>
        YaLoTenia = 2
    }

    /// <summary>NestoAPI#623 corte 4: lo que el importador lee y escribe en la BD (sustituible en los tests).</summary>
    public interface IRepositorioLeadsMeta
    {
        /// <summary>El cliente al que ya se importó este lead (ClientesOrigen.LeadExternoId), o null (también si no hay columna).</summary>
        Task<string> ClienteDelLead(string empresa, string lead);
        /// <summary>Un cliente activo (Estado &gt;= 0, cualquier contacto) con ese teléfono (9 cifras) o ese correo, o null.</summary>
        Task<ClienteParecidoLead> BuscarParecido(string empresa, string telefono, string email);
        /// <summary>
        /// Pone el origen con FechaOrigen = la del lead, SOLO si no tiene ninguno (nunca pisa otro). Si queda con este origen,
        /// guarda el id del lead y el consentimiento (si existen las columnas).
        /// </summary>
        Task<ResultadoOrigenLead> PonerOrigenLead(string empresa, string cliente, string origen, DateTime fechaOrigen, string lead, bool aceptaComunicaciones, string usuario);
        /// <summary>Las personas de contacto del cliente no reciben el boletín (EnviarBoletin = 0).</summary>
        Task QuitarBoletin(string empresa, string cliente);
    }

    /// <summary>NestoAPI#623 corte 4: el alta de clientes de siempre (GestorClientes), sustituible en los tests.</summary>
    public interface IAltaClientesLeads
    {
        /// <summary>Población, provincia, ruta y vendedores del CP; lanza <see cref="CodigoPostalNoExisteException"/> si no existe.</summary>
        Task<RespuestaDatosGeneralesClientes> DatosCodigoPostal(string codigoPostal);
        /// <summary>Da de alta el cliente como POST api/Clientes y devuelve su número.</summary>
        Task<string> Crear(ClienteCrear cliente);
        /// <summary>Publica el cliente a Odoo (nesto_sync) con su origen y su consentimiento.</summary>
        Task PublicarConOrigen(string empresa, string cliente, string origen, DateTime fechaOrigen, bool aceptaComunicaciones, string usuario);
    }

    /// <summary>
    /// NestoAPI#623 corte 4: convierte cada lead del fichero de Meta en un cliente con su origen (o le pone el origen al cliente que
    /// ya tenía ese teléfono o ese correo). Cada lead va por su lado: un fallo no para a los demás. En ensayo no escribe nada.
    /// </summary>
    public class ImportadorLeadsMeta
    {
        public const string CODIGO_CREAR = "CREAR";
        public const string CODIGO_CREADO = "CREADO";
        public const string CODIGO_YA_EXISTIA = "YA_EXISTIA";
        public const string CODIGO_ERROR = "ERROR";
        public const string APLICACION_ALTA = "LeadsMeta";
        public const string TEXTO_SIN_CONSENTIMIENTO = "No acepta correos comerciales.";

        private readonly IRepositorioLeadsMeta repositorio;
        private readonly IAltaClientesLeads alta;
        private readonly Action<Exception> registrarError;
        private readonly Func<DateTime> reloj;

        public ImportadorLeadsMeta(IRepositorioLeadsMeta repositorio, IAltaClientesLeads alta, Action<Exception> registrarError = null, Func<DateTime> reloj = null)
        {
            this.repositorio = repositorio;
            this.alta = alta;
            this.registrarError = registrarError ?? (ex => { });
            this.reloj = reloj ?? (() => DateTime.Now);
        }

        public async Task<ResultadoImportacionLeadsDTO> Importar(IList<LeadMeta> leads, string origen, bool ensayo, string usuario)
        {
            var resultado = new ResultadoImportacionLeadsDTO { Ensayo = ensayo, Origen = origen, Leidos = leads?.Count ?? 0 };
            // Repetidos dentro del mismo fichero (en ensayo no los encontraría la BD).
            var vistos = new Dictionary<string, ResultadoLeadMetaDTO>(StringComparer.OrdinalIgnoreCase);
            foreach (LeadMeta lead in leads ?? new List<LeadMeta>())
            {
                ResultadoLeadMetaDTO fila = Fila(lead);
                try
                {
                    await Procesar(lead, fila, origen, ensayo, usuario, vistos).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    fila.Codigo = CODIGO_ERROR;
                    fila.Accion = "Error";
                    fila.Motivo = string.IsNullOrWhiteSpace(fila.Cliente)
                        ? ex.Message
                        : $"El cliente {fila.Cliente} está creado, pero ha fallado después: {ex.Message}";
                    // Solo el id del lead: nada de nombre, teléfono ni correo en ELMAH.
                    registrarError(new Exception($"NestoAPI#623: no se ha podido importar el lead de Meta {lead?.Id} (fila {lead?.Fila}"
                        + (string.IsNullOrWhiteSpace(fila.Cliente) ? "" : $", cliente {fila.Cliente}") + $"): {ex.GetType().Name}", ex));
                }
                foreach (string clave in Claves(lead))
                {
                    if (!vistos.ContainsKey(clave))
                    {
                        vistos[clave] = fila;
                    }
                }
                resultado.Leads.Add(fila);
            }
            resultado.Creados = resultado.Leads.Count(l => l.Codigo == CODIGO_CREAR || l.Codigo == CODIGO_CREADO);
            resultado.YaExistian = resultado.Leads.Count(l => l.Codigo == CODIGO_YA_EXISTIA);
            resultado.Errores = resultado.Leads.Count(l => l.Codigo == CODIGO_ERROR);
            return resultado;
        }

        private async Task Procesar(LeadMeta lead, ResultadoLeadMetaDTO fila, string origen, bool ensayo, string usuario,
            Dictionary<string, ResultadoLeadMetaDTO> vistos)
        {
            string motivoInvalido = MotivoInvalido(lead);
            if (motivoInvalido != null)
            {
                Error(fila, motivoInvalido);
                return;
            }

            ResultadoLeadMetaDTO repetido = Claves(lead).Where(vistos.ContainsKey).Select(c => vistos[c]).FirstOrDefault();
            if (repetido != null)
            {
                YaExistia(fila, repetido.Cliente, $"Repetido en el fichero (fila {repetido.Fila}).");
                fila.Vendedor = repetido.Vendedor;
                return;
            }

            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            DateTime fechaOrigen = lead.FechaCreacion ?? reloj();

            string importado = await repositorio.ClienteDelLead(empresa, lead.Id).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(importado))
            {
                YaExistia(fila, importado.Trim(), "Este lead ya se importó.");
                return;
            }

            ClienteParecidoLead parecido = await repositorio.BuscarParecido(empresa, lead.TelefonoParaBuscar, lead.Email).ConfigureAwait(false);
            if (parecido != null)
            {
                await YaExistente(lead, fila, parecido, origen, fechaOrigen, ensayo, usuario).ConfigureAwait(false);
                return;
            }

            ClienteCrear clienteCrear = await PrepararAlta(lead, fila, usuario).ConfigureAwait(false);
            if (ensayo)
            {
                fila.Codigo = CODIGO_CREAR;
                fila.Accion = "Se creará";
                return;
            }

            fila.Cliente = (await alta.Crear(clienteCrear).ConfigureAwait(false))?.Trim();
            fila.Codigo = CODIGO_CREADO;
            fila.Accion = "Creado";
            _ = await repositorio.PonerOrigenLead(empresa, fila.Cliente, origen, fechaOrigen, lead.Id, lead.AceptaComunicaciones, usuario).ConfigureAwait(false);
            if (!lead.AceptaComunicaciones)
            {
                await repositorio.QuitarBoletin(empresa, fila.Cliente).ConfigureAwait(false);
            }
            await PublicarSinFallar(lead, fila, empresa, origen, fechaOrigen, usuario).ConfigureAwait(false);
        }

        private async Task YaExistente(LeadMeta lead, ResultadoLeadMetaDTO fila, ClienteParecidoLead parecido, string origen, DateTime fechaOrigen,
            bool ensayo, string usuario)
        {
            string cliente = parecido.Cliente?.Trim();
            fila.Vendedor = parecido.Vendedor?.Trim();
            string origenActual = string.IsNullOrWhiteSpace(parecido.Origen) ? null : parecido.Origen.Trim();
            if (origenActual != null && !string.Equals(origenActual, origen, StringComparison.OrdinalIgnoreCase))
            {
                YaExistia(fila, cliente, $"Ya existía con el origen {origenActual}: no se cambia.");
                return;
            }
            if (ensayo)
            {
                YaExistia(fila, cliente, origenActual == null ? "Ya existía: se le pondrá el origen." : "Ya existía con este origen.");
                return;
            }
            ResultadoOrigenLead puesto = await repositorio.PonerOrigenLead(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente, origen, fechaOrigen,
                lead.Id, lead.AceptaComunicaciones, usuario).ConfigureAwait(false);
            YaExistia(fila, cliente, puesto == ResultadoOrigenLead.Puesto ? "Ya existía: se le ha puesto el origen."
                : puesto == ResultadoOrigenLead.YaLoTenia ? "Ya existía con este origen."
                : "Ya existía con otro origen: no se cambia.");
            if (puesto != ResultadoOrigenLead.OtroOrigen)
            {
                await PublicarSinFallar(lead, fila, Constantes.Empresas.EMPRESA_POR_DEFECTO, origen, fechaOrigen, usuario).ConfigureAwait(false);
            }
        }

        /// <summary>La publicación a Odoo no deshace la importación: si falla, va a ELMAH (con el id del lead) y se dice en el motivo.</summary>
        private async Task PublicarSinFallar(LeadMeta lead, ResultadoLeadMetaDTO fila, string empresa, string origen, DateTime fechaOrigen, string usuario)
        {
            try
            {
                await alta.PublicarConOrigen(empresa, fila.Cliente, origen, fechaOrigen, lead.AceptaComunicaciones, usuario).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                fila.Motivo = string.IsNullOrWhiteSpace(fila.Motivo) ? "No se ha podido mandar a Odoo." : fila.Motivo + " No se ha podido mandar a Odoo.";
                registrarError(new Exception($"NestoAPI#623: lead de Meta {lead.Id} (cliente {fila.Cliente}) importado, pero no publicado a Odoo: {ex.GetType().Name}", ex));
            }
        }

        private async Task<ClienteCrear> PrepararAlta(LeadMeta lead, ResultadoLeadMetaDTO fila, string usuario)
        {
            var clienteCrear = new ClienteCrear
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Nombre = Recortar(lead.Nombre.Trim().ToUpper(), 50),
                Telefono = Recortar(lead.Telefono, 29),
                CodigoPostal = lead.CodigoPostal,
                // Sin NIF, como el alta de Nesto: «primera visita» (al completar el NIF pasa a su estado definitivo).
                Estado = Constantes.Clientes.Estados.PRIMERA_VISITA,
                Estetica = true,
                FormaPago = Constantes.FormasPago.TARJETA,
                PlazosPago = Constantes.PlazosPago.PREPAGO,
                Comentarios = Comentario(lead),
                Usuario = usuario,
                AplicacionAlta = APLICACION_ALTA,
                PersonasContacto = new List<PersonaContactoDTO>
                {
                    new PersonaContactoDTO
                    {
                        Nombre = Recortar(lead.Nombre.Trim(), 50),
                        CorreoElectronico = Recortar(lead.Email, 50),
                        EnviarBoletin = lead.AceptaComunicaciones
                    }
                }
            };
            if (string.IsNullOrWhiteSpace(lead.CodigoPostal))
            {
                return SinDatosDelCodigoPostal(clienteCrear, fila, AVISO_SIN_CP);
            }
            if (!LectorLeadsMeta.EsCodigoPostalValido(lead.CodigoPostal))
            {
                return SinDatosDelCodigoPostal(clienteCrear, fila, $"El código postal «{lead.CodigoPostal}» no es válido: se da de alta sin código postal y queda al vendedor general.");
            }
            RespuestaDatosGeneralesClientes datos;
            try
            {
                datos = await alta.DatosCodigoPostal(lead.CodigoPostal).ConfigureAwait(false);
            }
            catch (CodigoPostalNoExisteException)
            {
                return SinDatosDelCodigoPostal(clienteCrear, fila, $"El código postal {lead.CodigoPostal} no existe: se da de alta sin código postal y queda al vendedor general.");
            }
            clienteCrear.CodigoPostal = datos?.CodigoPostal ?? lead.CodigoPostal;
            clienteCrear.Poblacion = datos?.Poblacion;
            clienteCrear.Provincia = datos?.Provincia;
            clienteCrear.Ruta = datos?.Ruta?.Trim();
            clienteCrear.VendedorEstetica = string.IsNullOrWhiteSpace(datos?.VendedorEstetica) ? Constantes.Vendedores.VENDEDOR_GENERAL : datos.VendedorEstetica.Trim();
            clienteCrear.VendedorPeluqueria = datos?.VendedorPeluqueria?.Trim();
            fila.Vendedor = clienteCrear.VendedorEstetica;
            return clienteCrear;
        }

        public const string AVISO_SIN_CP = "Sin código postal: queda al vendedor general.";

        /// <summary>Sin CP (o con uno que no vale): sin población, provincia ni ruta, y al vendedor general. Se avisa en el motivo.</summary>
        private static ClienteCrear SinDatosDelCodigoPostal(ClienteCrear clienteCrear, ResultadoLeadMetaDTO fila, string aviso)
        {
            clienteCrear.CodigoPostal = null;
            clienteCrear.VendedorEstetica = Constantes.Vendedores.VENDEDOR_GENERAL;
            fila.Vendedor = clienteCrear.VendedorEstetica;
            fila.Motivo = aviso;
            return clienteCrear;
        }

        /// <summary>«Lead de Meta (Instagram) 05/11 21:48: tengo un centro de estética; solicita: una demostración. No acepta correos comerciales.»</summary>
        public static string Comentario(LeadMeta lead)
        {
            string plataforma = string.Equals(lead.Plataforma, "ig", StringComparison.OrdinalIgnoreCase) ? "Instagram"
                : string.Equals(lead.Plataforma, "fb", StringComparison.OrdinalIgnoreCase) ? "Facebook"
                : "Instagram/Facebook";
            string fecha = lead.FechaCreacion.HasValue ? " " + lead.FechaCreacion.Value.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture) : "";
            var partes = new List<string>();
            if (!string.IsNullOrWhiteSpace(lead.SituacionProfesional))
            {
                partes.Add(lead.SituacionProfesional);
            }
            if (!string.IsNullOrWhiteSpace(lead.Solicita))
            {
                partes.Add("solicita: " + lead.Solicita);
            }
            string texto = $"Lead de Meta ({plataforma}){fecha}" + (partes.Count > 0 ? ": " + string.Join("; ", partes) : "") + ".";
            return lead.AceptaComunicaciones ? texto : texto + " " + TEXTO_SIN_CONSENTIMIENTO;
        }

        internal static string MotivoInvalido(LeadMeta lead)
        {
            if (lead == null || string.IsNullOrWhiteSpace(lead.Id))
            {
                return "Falta el id del lead de Meta.";
            }
            if (string.IsNullOrWhiteSpace(lead.Nombre))
            {
                return "Falta el nombre.";
            }
            if (string.IsNullOrWhiteSpace(lead.TelefonoParaBuscar) && string.IsNullOrWhiteSpace(lead.Email))
            {
                return "No tiene teléfono ni correo.";
            }
            if (!string.IsNullOrWhiteSpace(lead.TelefonoParaBuscar) && lead.TelefonoParaBuscar.Length < 9)
            {
                return "El teléfono tiene menos de 9 cifras.";
            }
            return null;
        }

        private static IEnumerable<string> Claves(LeadMeta lead)
        {
            if (lead == null)
            {
                yield break;
            }
            if (!string.IsNullOrWhiteSpace(lead.Id))
            {
                yield return "id:" + lead.Id.Trim();
            }
            if (!string.IsNullOrWhiteSpace(lead.TelefonoParaBuscar) && lead.TelefonoParaBuscar.Length >= 9)
            {
                yield return "tel:" + lead.TelefonoParaBuscar;
            }
            if (!string.IsNullOrWhiteSpace(lead.Email))
            {
                yield return "email:" + lead.Email.Trim();
            }
        }

        private static ResultadoLeadMetaDTO Fila(LeadMeta lead) => new ResultadoLeadMetaDTO
        {
            Fila = lead?.Fila ?? 0,
            Lead = lead?.Id,
            FechaLead = lead?.FechaCreacion,
            Nombre = lead?.Nombre,
            Telefono = lead?.Telefono,
            Email = lead?.Email,
            CodigoPostal = lead?.CodigoPostal,
            AceptaComunicaciones = lead?.AceptaComunicaciones ?? false
        };

        private static void Error(ResultadoLeadMetaDTO fila, string motivo)
        {
            fila.Codigo = CODIGO_ERROR;
            fila.Accion = "Error";
            fila.Motivo = motivo;
        }

        private static void YaExistia(ResultadoLeadMetaDTO fila, string cliente, string motivo)
        {
            fila.Codigo = CODIGO_YA_EXISTIA;
            fila.Accion = "Ya existía";
            fila.Cliente = cliente;
            fila.Motivo = motivo;
        }

        private static string Recortar(string texto, int longitud)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            string t = texto.Trim();
            return t.Length > longitud ? t.Substring(0, longitud) : t;
        }
    }
}
