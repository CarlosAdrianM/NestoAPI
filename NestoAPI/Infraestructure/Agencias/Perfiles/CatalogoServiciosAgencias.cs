using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Perfiles
{
    /// <summary>
    /// NestoAPI#546: servicios, horarios y tipos de retorno que existen en UNA agencia (los valores de
    /// EnviosAgencia.Servicio/Horario/Retorno), con su nombre para los mensajes. Hasta ahora esta
    /// lista solo vivía en las clases Agencia*.vb de Nesto (ListaServicios, ListaHorarios,
    /// ListaTiposRetorno); el 28/09/26 se guardaron envíos de GLS con el servicio 48 / horario 0 de CTT
    /// y la API no los paró.
    /// </summary>
    public class CatalogoServiciosAgencia
    {
        public CatalogoServiciosAgencia(string nombreAgencia,
            IDictionary<short, string> servicios,
            IDictionary<short, string> horarios,
            IDictionary<short, string> retornos)
        {
            NombreAgencia = nombreAgencia;
            Servicios = new Dictionary<short, string>(servicios);
            Horarios = new Dictionary<short, string>(horarios);
            Retornos = new Dictionary<short, string>(retornos);
        }

        public string NombreAgencia { get; }
        public IReadOnlyDictionary<short, string> Servicios { get; }
        public IReadOnlyDictionary<short, string> Horarios { get; }
        public IReadOnlyDictionary<short, string> Retornos { get; }

        /// <summary>Mensaje con lo que no existe en la agencia, o null si servicio, horario y retorno son suyos.</summary>
        public string Validar(short servicio, short horario, short retorno)
        {
            var errores = new List<string>();
            if (!Servicios.ContainsKey(servicio))
            {
                errores.Add($"el servicio {servicio} (válidos: {Describir(Servicios)})");
            }
            if (!Horarios.ContainsKey(horario))
            {
                errores.Add($"el horario {horario} (válidos: {Describir(Horarios)})");
            }
            if (!Retornos.ContainsKey(retorno))
            {
                errores.Add($"el retorno {retorno} (válidos: {Describir(Retornos)})");
            }
            if (errores.Count == 0)
            {
                return null;
            }
            return $"La agencia {NombreAgencia} no tiene {string.Join(", ni ", errores)}. " +
                "Revisa el servicio, el horario y el retorno del envío (¿son de otra agencia?) antes de guardarlo.";
        }

        private static string Describir(IReadOnlyDictionary<short, string> valores)
            => string.Join(", ", valores.OrderBy(v => v.Key).Select(v => string.IsNullOrWhiteSpace(v.Value) ? v.Key.ToString() : $"{v.Key} {v.Value}"));
    }

    /// <summary>
    /// NestoAPI#546 — capacidad: la agencia declara su catálogo de servicios/horarios/retornos y la API
    /// rechaza un envío con valores que no son suyos. Las agencias sin esta capacidad (históricas o sin
    /// catálogo: Sending, Glovo, OnTime...) no se validan, para no romper los envíos viejos al modificarlos.
    /// </summary>
    public interface IPerfilConCatalogoServicios : IPerfilAgencia
    {
        CatalogoServiciosAgencia CatalogoServicios { get; }
    }

    /// <summary>
    /// NestoAPI#546: guarda del servidor para que ningún envío se guarde con un servicio, horario o retorno
    /// que su agencia no tiene (por ningún camino: POST, PUT, CrearEtiquetaPendiente y tramitación remota).
    /// El catálogo de cada agencia vive en su perfil (<see cref="IPerfilConCatalogoServicios"/>); aquí
    /// solo se decide CUÁNDO se valida y se consulta el perfil. Puro (sin BBDD).
    /// </summary>
    public static class CatalogoServiciosAgencias
    {
        // Registro SIN puerta: el catálogo aplica también a las agencias en cuarentena o sombra (CEX, CTT).
        private static readonly RegistroAgencias _perfiles = RegistroAgencias.PorReflexionSinPuerta();

        /// <summary>Catálogo de la agencia, o null si la agencia no tiene (no se valida).</summary>
        public static CatalogoServiciosAgencia De(int agencia)
            => (_perfiles.Perfil(agencia) as IPerfilConCatalogoServicios)?.CatalogoServicios;

        /// <summary>Mensaje de error si el servicio/horario/retorno no son de la agencia; null si lo son o si la agencia no tiene catálogo.</summary>
        public static string Validar(int agencia, short servicio, short horario, short retorno)
            => De(agencia)?.Validar(servicio, horario, retorno);

        /// <summary>Atajo para validar un envío completo (alta, etiqueta pendiente, tramitación remota).</summary>
        public static string Validar(EnviosAgencia envio)
            => envio == null ? null : Validar(envio.Agencia, envio.Servicio, envio.Horario, envio.Retorno);

        /// <summary>
        /// ¿Hay que validar un PUT? Se valida si el envío está PENDIENTE en la BD (es lo que se va a
        /// imprimir y tramitar: el caso del 28/09) o si el PUT cambia la agencia, el servicio, el horario
        /// o el retorno. Un envío ya tramitado que se modifica por otras razones (reembolso, dirección,
        /// estado) no se bloquea por datos históricos (p. ej. GLS con horario 0 o CEX con retorno 1).
        /// </summary>
        public static bool DebeValidarModificacion(short estadoBD, int agenciaBD, short servicioBD, short horarioBD, short retornoBD, EnviosAgencia nuevo)
        {
            return estadoBD < Constantes.Agencias.ESTADO_EN_CURSO
                || agenciaBD != nuevo.Agencia
                || servicioBD != nuevo.Servicio
                || horarioBD != nuevo.Horario
                || retornoBD != nuevo.Retorno;
        }
    }
}
