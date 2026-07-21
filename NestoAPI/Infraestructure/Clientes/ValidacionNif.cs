using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#327: estado de validación del NIF de una ficha contra el censo de la AEAT.
    /// SIN_VALIDAR incluye el caso "hay registro pero la ficha cambió de NIF/nombre después"
    /// (la fila deja de casar y la validación caduca sola, sin hooks en cada camino de
    /// modificación). EXCLUIDO = clientes de facturas simplificadas (Amazon, tienda online,
    /// público final): su NIF es ficticio a propósito y van como F2 sin destinatario (#325).
    /// </summary>
    public enum EstadoValidacionNif
    {
        SinValidar,
        Correcto,
        Incorrecto,
        Excluido
    }

    /// <summary>Fila de la tabla ValidacionesNif (satélite de Clientes, ver script #327).</summary>
    public class ValidacionNifRegistro
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nif { get; set; }
        public string Nombre { get; set; }
        public string Estado { get; set; }          // "CORRECTO" / "INCORRECTO"
        public string ResultadoAeat { get; set; }
        public DateTime FechaValidacion { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>Resultado de consultar/validar el NIF de una ficha.</summary>
    public class ResultadoValidacionNif
    {
        public EstadoValidacionNif Estado { get; set; }
        public string Nif { get; set; }
        public string Nombre { get; set; }
        public string ResultadoAeat { get; set; }
        /// <summary>True solo cuando ESTA llamada ha pasado el estado a Incorrecto
        /// (transición SinValidar → Incorrecto): es el momento de avisar por correo,
        /// una sola vez, no en cada pedido del mismo cliente.</summary>
        public bool AcabaDeResultarIncorrecto { get; set; }
    }

    /// <summary>
    /// Acceso a la tabla ValidacionesNif por SQL crudo (no está en el EDMX). Interfaz aparte
    /// para que la lógica de ServicioValidacionNif sea testeable sin BD (patrón
    /// IAlmacenRectificativasPendientes de #87).
    /// </summary>
    public interface IAlmacenValidacionesNif
    {
        Task<ValidacionNifRegistro> Leer(string empresa, string cliente, string contacto);
        Task Guardar(ValidacionNifRegistro registro);
    }

    public interface IServicioValidacionNif
    {
        /// <summary>Estado efectivo SIN llamar a la AEAT (compara ficha vs registro).</summary>
        Task<ResultadoValidacionNif> ObtenerEstado(string empresa, string cliente, string contacto);

        /// <summary>
        /// Estado efectivo, validando contra la AEAT si está SinValidar y registrando el
        /// veredicto. Nunca lanza por fallo del servicio de la AEAT (devuelve SinValidar:
        /// ya se validará en el siguiente intento).
        /// </summary>
        Task<ResultadoValidacionNif> ValidarSiHaceFalta(string empresa, string cliente, string contacto, string usuario);

        /// <summary>
        /// Valida la ficha del CLIENTE PRINCIPAL (empresa por defecto), que es de donde salen
        /// los datos fiscales que se declaran en la factura (PersistirDatosFiscalesFactura).
        /// </summary>
        Task<ResultadoValidacionNif> ValidarPrincipal(string cliente, string usuario);

        /// <summary>
        /// "Ponerlo en un sitio y se arregla todo": valida el NIF nuevo contra la AEAT (con el
        /// nombre del principal) y, SOLO si es correcto, lo escribe en TODOS los contactos del
        /// cliente (#330) y registra la validación. Si la AEAT lo rechaza, no se toca nada y
        /// se devuelve el motivo. Los pedidos pendientes no hay que tocarlos: los datos
        /// fiscales se copian de la ficha al facturar.
        /// </summary>
        Task<ResultadoCorreccionNif> CorregirNif(string cliente, string nifNuevo, string usuario);

        /// <summary>
        /// NestoAPI#330: propaga el NIF del cliente principal a los contactos que tengan otro
        /// distinto (erratas de tecleo), SOLO si el del principal está validado como correcto
        /// contra la AEAT — nunca se extiende un dato posiblemente malo. Audita cada cambio.
        /// Devuelve cuántos contactos se han corregido.
        /// </summary>
        Task<int> UnificarNifContactos(string cliente, string usuario);

        /// <summary>
        /// Listado para las pantallas de corrección (Nesto#417 / NestoApp#157): fichas cuya
        /// validación VIGENTE es incorrecta (si la ficha cambió de NIF después, ya no sale:
        /// está "sin validar"), priorizando las que tienen pedido pendiente de servir o
        /// facturar. Filtro opcional por vendedor (permisos por rol en el cliente).
        /// </summary>
        Task<System.Collections.Generic.List<ClienteNifIncorrectoDTO>> ListarNifIncorrectos(string vendedor = null);
    }

    /// <summary>Fila del listado de NIF incorrectos (#327, para Nesto#417/NestoApp#157).</summary>
    public class ClienteNifIncorrectoDTO
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Nif { get; set; }
        public string ResultadoAeat { get; set; }
        public DateTime FechaValidacion { get; set; }
        public string Vendedor { get; set; }
        /// <summary>Prioritario: su factura se va a encontrar el problema.</summary>
        public bool TienePedidoPendiente { get; set; }
    }

    /// <summary>Resultado de la corrección centralizada del NIF (#327/Nesto#417).</summary>
    public class ResultadoCorreccionNif
    {
        public bool Corregido { get; set; }
        public string Nif { get; set; }
        public string ResultadoAeat { get; set; }
        public string NombreAeat { get; set; }
        public int ContactosActualizados { get; set; }
        public string Motivo { get; set; }
    }
}
