using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26): la plaza de destino de un envío (EnviosAgencia.Nemonico, NombrePlaza, TelefonoPlaza y
    /// EmailPlaza), como la rellena IAgencia.calcularPlaza en Nesto.
    /// </summary>
    public class PlazaAgencia
    {
        /// <summary>Teléfono de plaza: como mucho 27 caracteres (AgenciasViewModel.InsertarRegistro y AgenciaASM.calcularPlaza).</summary>
        public const int LONGITUD_TELEFONO = 27;

        public string Nemonico { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        /// <summary>Sin plaza (GLS no contesta): Nesto deja los campos como estaban, normalmente vacíos.</summary>
        public static PlazaAgencia Vacia() => new PlazaAgencia();

        public bool EsVacia => string.IsNullOrWhiteSpace(Nemonico) && string.IsNullOrWhiteSpace(Nombre)
            && string.IsNullOrWhiteSpace(Telefono) && string.IsNullOrWhiteSpace(Email);

        /// <summary>El teléfono recortado a <see cref="LONGITUD_TELEFONO"/> (null pasa a vacío).</summary>
        public static string RecortarTelefono(string telefono)
        {
            if (string.IsNullOrEmpty(telefono))
            {
                return string.Empty;
            }
            return telefono.Length <= LONGITUD_TELEFONO ? telefono : telefono.Substring(0, LONGITUD_TELEFONO);
        }
    }

    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26): plaza y código de barras de UNA agencia, en el servidor. Es lo que hacen hoy
    /// IAgencia.calcularPlaza y IAgencia.calcularCodigoBarras de Nesto (AgenciaASM, AgenciaCanteras, AgenciaGestionadaPorApi), para que el envío se pueda crear desde el pedido sin pasar por Nesto.
    /// </summary>
    public interface IPlazaYCodigoBarrasAgencia
    {
        /// <summary>Los números de agencia (AgenciasTransporte.Numero) que atiende.</summary>
        IReadOnlyCollection<int> Agencias { get; }

        /// <summary>True si la plaza se pide a la agencia (GLS: GetPlazaXCP, hasta 15 s); false si es fija.</summary>
        bool PlazaConsultaLaAgencia { get; }

        /// <summary>La plaza del destino. Nunca lanza: si no se puede saber, <see cref="PlazaAgencia.Vacia"/>.</summary>
        Task<PlazaAgencia> CalcularPlazaAsync(string codPostal, int pais);

        /// <summary>
        /// El código de barras del envío YA grabado (hace falta su Numero). "" si la agencia no lo calcula en local (lo
        /// asigna al tramitar o lo da a mano); null si hace falta el prefijo de la agencia y no lo tiene.
        /// </summary>
        string CalcularCodigoBarras(EnviosAgencia envio, string prefijoAgencia);
    }

    /// <summary>NestoAPI#595: la agencia no tiene plaza ni código de barras en el servidor (Correos Express, Glovo, OnTime, Sending...).</summary>
    public class AgenciaSinPlazaYCodigoBarrasException : NotSupportedException
    {
        public AgenciaSinPlazaYCodigoBarrasException(int agencia)
            : base($"La agencia {agencia} no está soportada para calcular la plaza y el código de barras en el servidor.")
        {
            Agencia = agencia;
        }

        public int Agencia { get; }
    }

    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26): elige la implementación de plaza y código de barras por número de agencia. Las
    /// agencias que no están (Correos Express, Glovo, OnTime, Sending) dan <see cref="AgenciaSinPlazaYCodigoBarrasException"/>.
    /// </summary>
    public class RegistroPlazasAgencias
    {
        private readonly Dictionary<int, IPlazaYCodigoBarrasAgencia> _porAgencia = new Dictionary<int, IPlazaYCodigoBarrasAgencia>();

        public RegistroPlazasAgencias(IEnumerable<IPlazaYCodigoBarrasAgencia> implementaciones)
        {
            foreach (IPlazaYCodigoBarrasAgencia implementacion in implementaciones ?? Enumerable.Empty<IPlazaYCodigoBarrasAgencia>())
            {
                foreach (int agencia in implementacion.Agencias)
                {
                    if (_porAgencia.ContainsKey(agencia))
                    {
                        throw new InvalidOperationException($"Hay más de una implementación de plaza y código de barras para la agencia {agencia}.");
                    }
                    _porAgencia.Add(agencia, implementacion);
                }
            }
        }

        /// <summary>
        /// Las agencias de hoy: GLS (1 y la de la empresa espejo, 5), Canteras (11), Innovatrans (12) y CTT (13). Correos
        /// Express (8) se quitó el 20/10/26 (ya no se usa): es una agencia no soportada más.
        /// </summary>
        /// <param name="clienteGls">El transporte de GetPlazaXCP (null = el real, contra asmred).</param>
        /// <param name="registrar">Dónde se registran los fallos de la plaza de GLS (null = ELMAH).</param>
        public static RegistroPlazasAgencias PorDefecto(IClientePlazaGls clienteGls = null, Action<Exception> registrar = null)
            => new RegistroPlazasAgencias(new IPlazaYCodigoBarrasAgencia[]
            {
                new PlazaYCodigoBarrasGls(clienteGls ?? new ClientePlazaGls(), registrar),
                PlazaYCodigoBarrasFija.Canteras(),
                PlazaYCodigoBarrasFija.Innovatrans(),
                PlazaYCodigoBarrasFija.Ctt()
            });

        public bool Soporta(int agencia) => _porAgencia.ContainsKey(agencia);

        /// <exception cref="AgenciaSinPlazaYCodigoBarrasException">Si la agencia no está soportada.</exception>
        public IPlazaYCodigoBarrasAgencia Para(int agencia)
        {
            if (!_porAgencia.TryGetValue(agencia, out IPlazaYCodigoBarrasAgencia implementacion))
            {
                throw new AgenciaSinPlazaYCodigoBarrasException(agencia);
            }
            return implementacion;
        }
    }

    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26): agencias con plaza fija y sin código de barras en local: Canteras (AgenciaCanteras.vb:
    /// «CN», el nº de envío llega por correo) y las gestionadas por la API (AgenciaGestionadaPorApi.vb l.221-231: CTT «CT»,
    /// Innovatrans «IN», con el nombre de la agencia; el albarán lo da /Tramitar).
    /// </summary>
    public class PlazaYCodigoBarrasFija : IPlazaYCodigoBarrasAgencia
    {
        private readonly PlazaAgencia _plaza;

        public PlazaYCodigoBarrasFija(int agencia, string nemonico, string nombre)
        {
            Agencias = new[] { agencia };
            _plaza = new PlazaAgencia { Nemonico = nemonico, Nombre = nombre };
        }

        public static PlazaYCodigoBarrasFija Canteras() => new PlazaYCodigoBarrasFija(Constantes.Agencias.AGENCIA_CANTERAS, "CN", "Canteras");
        public static PlazaYCodigoBarrasFija Innovatrans() => new PlazaYCodigoBarrasFija(Constantes.Agencias.AGENCIA_INNOVATRANS, "IN", "Innovatrans");
        public static PlazaYCodigoBarrasFija Ctt() => new PlazaYCodigoBarrasFija(Constantes.Agencias.AGENCIA_CTT, "CT", "CTT");

        public IReadOnlyCollection<int> Agencias { get; }

        public bool PlazaConsultaLaAgencia => false;

        public Task<PlazaAgencia> CalcularPlazaAsync(string codPostal, int pais)
            => Task.FromResult(new PlazaAgencia { Nemonico = _plaza.Nemonico, Nombre = _plaza.Nombre });

        public string CalcularCodigoBarras(EnviosAgencia envio, string prefijoAgencia) => string.Empty;
    }

    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26, sombra): compara la plaza y el código de barras que ha grabado Nesto con los que
    /// calcularía el servidor. Puro, para probarlo sin BD. Los textos se comparan sin espacios de relleno.
    /// </summary>
    public static class ComparadorPlazaYCodigoBarras
    {
        /// <param name="compararCodigo">False en las agencias que no calculan el código en local (Canteras, CTT, Innovatrans).</param>
        /// <param name="codigoEsperado">El del servidor; null si la agencia no tiene prefijo.</param>
        /// <param name="plazaEsperada">La del servidor; null = no se compara (GLS, para no llamar a GLS en el PUT).</param>
        public static List<string> Diferencias(EnviosAgencia envio, bool compararCodigo, string codigoEsperado, PlazaAgencia plazaEsperada)
        {
            var diferencias = new List<string>();
            if (envio == null)
            {
                return diferencias;
            }
            if (compararCodigo)
            {
                Comparar(diferencias, "código de barras", envio.CodigoBarras, codigoEsperado ?? "(sin prefijo en la agencia)");
            }
            if (plazaEsperada != null)
            {
                Comparar(diferencias, "nemónico", envio.Nemonico, plazaEsperada.Nemonico);
                Comparar(diferencias, "nombre de plaza", envio.NombrePlaza, plazaEsperada.Nombre);
                Comparar(diferencias, "teléfono de plaza", envio.TelefonoPlaza, plazaEsperada.Telefono);
                Comparar(diferencias, "email de plaza", envio.EmailPlaza, plazaEsperada.Email);
            }
            return diferencias;
        }

        private static void Comparar(List<string> diferencias, string campo, string nesto, string servidor)
        {
            string a = nesto?.Trim() ?? string.Empty;
            string b = servidor?.Trim() ?? string.Empty;
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                diferencias.Add($"{campo}: Nesto={a} / servidor={b}");
            }
        }
    }

    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26): aviso informativo (no es un error) de que la plaza o el código de barras que ha grabado
    /// Nesto no coinciden con los del servidor. Las coincidencias NO se registran.
    /// </summary>
    public class PlazaCodigoBarrasDifiereInfo : Exception
    {
        public PlazaCodigoBarrasDifiereInfo(string mensaje) : base(mensaje) { }

        public static PlazaCodigoBarrasDifiereInfo Crear(EnviosAgencia envio, IReadOnlyCollection<string> diferencias)
            => new PlazaCodigoBarrasDifiereInfo($"[Plaza y código de barras #595] Envío {envio.Numero} (agencia {envio.Agencia}, " +
                $"pedido {envio.Pedido}): lo que calcularía el servidor difiere de lo que ha grabado Nesto en {diferencias.Count} " +
                $"{(diferencias.Count == 1 ? "campo" : "campos")}: {string.Join("; ", diferencias)}");
    }
}
