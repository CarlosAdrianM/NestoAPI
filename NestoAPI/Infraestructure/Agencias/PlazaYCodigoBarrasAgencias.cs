using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// IAgencia.calcularPlaza y IAgencia.calcularCodigoBarras de Nesto (AgenciaASM, AgenciaCorreosExpress, AgenciaCanteras,
    /// AgenciaGestionadaPorApi), para que el envío se pueda crear desde el pedido sin pasar por Nesto.
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

    /// <summary>NestoAPI#595: la agencia no tiene plaza ni código de barras en el servidor (Glovo, OnTime, Sending...).</summary>
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
    /// agencias que no están (Glovo, OnTime, Sending) dan <see cref="AgenciaSinPlazaYCodigoBarrasException"/>.
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
        /// Las agencias de hoy: GLS (1 y la de la empresa espejo, 5), Correos Express (8), Canteras (11), Innovatrans (12) y
        /// CTT (13).
        /// </summary>
        /// <param name="clienteGls">El transporte de GetPlazaXCP (null = el real, contra asmred).</param>
        /// <param name="registrar">Dónde se registran los fallos de la plaza de GLS (null = ELMAH).</param>
        public static RegistroPlazasAgencias PorDefecto(IClientePlazaGls clienteGls = null, Action<Exception> registrar = null)
            => new RegistroPlazasAgencias(new IPlazaYCodigoBarrasAgencia[]
            {
                new PlazaYCodigoBarrasGls(clienteGls ?? new ClientePlazaGls(), registrar),
                new PlazaYCodigoBarrasCorreosExpress(),
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
    /// NestoAPI#595 (slice 2, 20/10/26): Correos Express, como AgenciaCorreosExpress.vb. Plaza fija (l.140-145) y código de
    /// barras de 16 cifras con dígito de control (l.441-471 y 494-508), sin llamadas externas.
    /// </summary>
    public class PlazaYCodigoBarrasCorreosExpress : IPlazaYCodigoBarrasAgencia
    {
        public const string NEMONICO = "CE";
        public const string NOMBRE = "Correos Express";
        public const string TELEFONO = "916602444";
        public const string EMAIL = "am-cabelloandres@correosexpress.com";

        public IReadOnlyCollection<int> Agencias { get; } = new[] { Constantes.Agencias.AGENCIA_CORREOS_EXPRESS };

        public bool PlazaConsultaLaAgencia => false;

        public Task<PlazaAgencia> CalcularPlazaAsync(string codPostal, int pais)
            => Task.FromResult(new PlazaAgencia { Nemonico = NEMONICO, Nombre = NOMBRE, Telefono = TELEFONO, Email = EMAIL });

        /// <summary>
        /// Servicio (2 cifras) + cliente etiquetador (las 4 primeras del prefijo de la agencia) + número del envío (9 cifras) +
        /// dígito de control. Null si la agencia no tiene un prefijo de al menos 4 caracteres (en Nesto revienta).
        /// <para>OJO, diferencia con VB: Nesto hace <c>posiciones(0) = envio.Servicio / 10</c>, que es división decimal y
        /// CInt con redondeo bancario (63 / 10 = 6,3 → 6, pero 57 / 10 = 5,7 → 6 y 96 / 10 = 9,6 → 10). Aquí es división
        /// entera (la decena del servicio, que es lo que pide el formato). Para los servicios que se usan de verdad (54, 63,
        /// 90, 93: unidades menores que 5) dan lo mismo; con unidades de 5 en adelante Nesto calcularía mal.</para>
        /// </summary>
        public string CalcularCodigoBarras(EnviosAgencia envio, string prefijoAgencia)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            if (prefijoAgencia == null || prefijoAgencia.Length < 4)
            {
                return null;
            }

            string numero = envio.Numero.ToString("D9", CultureInfo.InvariantCulture);
            var posiciones = new int[15];
            // Posiciones 1 y 2: código del producto (servicio).
            posiciones[0] = envio.Servicio / 10;
            posiciones[1] = envio.Servicio % 10;
            // Posiciones 3 a 6: código de cliente etiquetador (lo da Correos Express).
            for (int i = 0; i < 4; i++)
            {
                posiciones[2 + i] = Cifra(prefijoAgencia[i]);
            }
            // Posiciones 7 a 15: rango de expediciones (el número del envío).
            for (int i = 0; i < 9; i++)
            {
                posiciones[6 + i] = Cifra(numero[i]);
            }

            return string.Concat(posiciones.Select(p => p.ToString(CultureInfo.InvariantCulture)))
                + DigitoControl(posiciones).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// AgenciaCorreosExpress.CalcularDigitoControl: suma ponderada en la que la posición i lleva peso 1 si
        /// (i + longitud) es par y 3 si es impar (con 15 posiciones: 3 en las pares desde 0, 1 en las impares); el dígito es
        /// lo que falta hasta la siguiente decena (0 si ya es múltiplo de 10).
        /// </summary>
        public static int DigitoControl(IReadOnlyList<int> posiciones)
        {
            int suma = 0;
            for (int i = 0; i < posiciones.Count; i++)
            {
                suma += (i + posiciones.Count) % 2 == 0 ? posiciones[i] : posiciones[i] * 3;
            }
            int resto = suma % 10;
            return resto == 0 ? 0 : 10 - resto;
        }

        // Val(carácter) de VB: la cifra, o 0 si no es una cifra.
        private static int Cifra(char caracter) => caracter >= '0' && caracter <= '9' ? caracter - '0' : 0;
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
}
