using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Embudo
{
    /// <summary>Entrada de POST api/Clientes/Sync/Republicar. Lo que no venga, su valor por defecto.</summary>
    public class PeticionRepublicarEmbudo
    {
        /// <summary>Obligatorio (p. ej. NV-2026-11-01), 30 caracteres como mucho.</summary>
        public string Bloque { get; set; }
        /// <summary>Por defecto "NV".</summary>
        public string Vendedor { get; set; }
        /// <summary>Por defecto [0, 7, 9, 93, 11, 22].</summary>
        public List<short> Estados { get; set; }
        /// <summary>Por defecto true.</summary>
        public bool? SoloConCorreo { get; set; }
        /// <summary>Por defecto 50; más de 50 → 400.</summary>
        public int? Maximo { get; set; }
        /// <summary>Por defecto TRUE: solo cuenta y lista, no escribe nada.</summary>
        public bool? Ensayo { get; set; }
    }

    public class ResultadoRepublicarEmbudo
    {
        public string Bloque { get; set; }
        public bool Ensayo { get; set; }
        /// <summary>Clientes encolados (o que se encolarían, en ensayo).</summary>
        public int ClientesEncolados { get; set; }
        /// <summary>Contactos de los clientes del bloque (también los anulados).</summary>
        public int Contactos { get; set; }
        public int ContactosActivos { get; set; }
        /// <summary>Personas de contacto activas, de contactos activos, con «@» en el correo y EnviarBoletin.</summary>
        public int PersonasConCorreo { get; set; }
        /// <summary>Cumplen las reglas pero ya están en el registro (ya entraron en el embudo).</summary>
        public int ExcluidosPorYaEnviados { get; set; }
        /// <summary>Cumplen las reglas pero tienen una fila pendiente en Nesto_sync (entrarán en otro bloque).</summary>
        public int ExcluidosPorPendienteSincronizar { get; set; }
        /// <summary>Cumplen todo y no caben en este bloque.</summary>
        public int CandidatosQueQuedan { get; set; }
        /// <summary>Nº de cliente de los del bloque, en el orden en que entran.</summary>
        public List<string> Clientes { get; set; } = new List<string>();
    }

    public class PeticionSembrarEmbudo
    {
        /// <summary>Por defecto TRUE: solo dice qué haría.</summary>
        public bool? Ensayo { get; set; }
        public List<FilaSiembraEmbudo> Filas { get; set; }
    }

    public class ResultadoSembrarEmbudo
    {
        public bool Ensayo { get; set; }
        public int Recibidas { get; set; }
        /// <summary>Las que entran (o entrarían) en el registro.</summary>
        public int Nuevas { get; set; }
        public int YaEstaban { get; set; }
        /// <summary>Nº de cliente que no existen en la empresa 1 (no se meten).</summary>
        public List<string> NoExisten { get; set; } = new List<string>();
    }

    /// <summary>Un error de la petición (400) con el texto para el usuario.</summary>
    public class PeticionEmbudoNoValidaException : Exception
    {
        public PeticionEmbudoNoValidaException(string mensaje) : base(mensaje) { }
    }

    /// <summary>
    /// Embudo de Odoo con clientes del vendedor general (odoo-custom-addons#45): elige un bloque de clientes y lo republica a Odoo
    /// por Nesto_sync, una sola vez por cliente. Odoo decide el ritmo, deduplica por correo y manda los correos.
    /// </summary>
    public class ServicioEmbudoClientes
    {
        public const int MAXIMO_POR_BLOQUE = 50;
        public const int MAXIMO_FILAS_SIEMBRA = 5000;
        public const int LONGITUD_MAXIMA_BLOQUE = 30;
        public const int LONGITUD_MAXIMA_ORIGEN = 30;
        public static readonly IReadOnlyList<short> ESTADOS_POR_DEFECTO = new short[] { 0, 7, 9, 93, 11, 22 };

        public static string MensajeSinTabla =>
            $"Falta lanzar el script {RepositorioClientesEmbudoSql.NOMBRE_SCRIPT} (tabla dbo.ClientesEmbudo): sin él no se puede usar el embudo.";

        private readonly IRepositorioClientesEmbudo repositorio;

        public ServicioEmbudoClientes(IRepositorioClientesEmbudo repositorio)
        {
            this.repositorio = repositorio;
        }

        /// <param name="maximoPermitido">El tope del bloque: <see cref="MAXIMO_POR_BLOQUE"/> a mano; el job diario (Carlos 11/11/26)
        /// admite hasta su <see cref="EmbudoBloqueDiarioJobsService.TAMANO_MAXIMO"/>.</param>
        public async Task<ResultadoRepublicarEmbudo> Republicar(PeticionRepublicarEmbudo peticion, string usuario, int maximoPermitido = MAXIMO_POR_BLOQUE)
        {
            if (peticion == null)
            {
                throw new PeticionEmbudoNoValidaException("Falta la petición.");
            }
            string bloque = peticion.Bloque?.Trim();
            if (string.IsNullOrEmpty(bloque))
            {
                throw new PeticionEmbudoNoValidaException("Falta el bloque (p. ej. NV-2026-11-01).");
            }
            if (bloque.Length > LONGITUD_MAXIMA_BLOQUE)
            {
                throw new PeticionEmbudoNoValidaException($"El bloque puede tener como mucho {LONGITUD_MAXIMA_BLOQUE} caracteres.");
            }
            if (string.Equals(bloque, RepositorioClientesEmbudoSql.BLOQUE_SIEMBRA, StringComparison.OrdinalIgnoreCase))
            {
                throw new PeticionEmbudoNoValidaException($"«{RepositorioClientesEmbudoSql.BLOQUE_SIEMBRA}» está reservado para la siembra del registro.");
            }
            int maximo = peticion.Maximo ?? MAXIMO_POR_BLOQUE;
            if (maximo < 1 || maximo > maximoPermitido)
            {
                throw new PeticionEmbudoNoValidaException($"El máximo tiene que estar entre 1 y {maximoPermitido}: el embudo se llena poco a poco.");
            }
            List<short> estados = peticion.Estados == null ? ESTADOS_POR_DEFECTO.ToList() : peticion.Estados.Distinct().ToList();
            if (estados.Count == 0)
            {
                throw new PeticionEmbudoNoValidaException("La lista de estados no puede ir vacía (sin ella: 0, 7, 9, 93, 11 y 22).");
            }
            string vendedor = string.IsNullOrWhiteSpace(peticion.Vendedor) ? Constantes.Vendedores.VENDEDOR_GENERAL : peticion.Vendedor.Trim();
            bool soloConCorreo = peticion.SoloConCorreo ?? true;
            bool ensayo = peticion.Ensayo ?? true;

            if (!await repositorio.HayTabla().ConfigureAwait(false))
            {
                throw new PeticionEmbudoNoValidaException(MensajeSinTabla);
            }

            List<CandidatoEmbudo> candidatos = await repositorio.LeerCandidatos(vendedor, estados, soloConCorreo).ConfigureAwait(false)
                ?? new List<CandidatoEmbudo>();
            List<CandidatoEmbudo> elegibles = candidatos.Where(c => !c.YaEnviado && !c.PendienteSincronizar).ToList();
            List<CandidatoEmbudo> delBloque = elegibles.Take(maximo).ToList();

            var resultado = new ResultadoRepublicarEmbudo
            {
                Bloque = bloque,
                Ensayo = ensayo,
                Contactos = delBloque.Sum(c => c.Contactos),
                ContactosActivos = delBloque.Sum(c => c.ContactosActivos),
                PersonasConCorreo = delBloque.Sum(c => c.PersonasConCorreo),
                ExcluidosPorYaEnviados = candidatos.Count(c => c.YaEnviado),
                ExcluidosPorPendienteSincronizar = candidatos.Count(c => !c.YaEnviado && c.PendienteSincronizar),
                CandidatosQueQuedan = elegibles.Count - delBloque.Count,
                Clientes = delBloque.Select(c => c.Cliente?.Trim()).ToList(),
                ClientesEncolados = delBloque.Count
            };
            if (!ensayo && delBloque.Count > 0)
            {
                // Si otro lo ha metido entre la lectura y la escritura, no entra dos veces: cuenta lo que entró de verdad
                resultado.ClientesEncolados = await repositorio.Registrar(bloque, resultado.Clientes, usuario).ConfigureAwait(false);
            }
            return resultado;
        }

        public async Task<ResultadoSembrarEmbudo> Sembrar(PeticionSembrarEmbudo peticion, string usuario)
        {
            List<FilaSiembraEmbudo> recibidas = peticion?.Filas ?? new List<FilaSiembraEmbudo>();
            if (recibidas.Count == 0)
            {
                throw new PeticionEmbudoNoValidaException("No ha llegado ninguna fila.");
            }
            if (recibidas.Count > MAXIMO_FILAS_SIEMBRA)
            {
                throw new PeticionEmbudoNoValidaException($"Como mucho {MAXIMO_FILAS_SIEMBRA} filas por petición.");
            }
            for (int i = 0; i < recibidas.Count; i++)
            {
                FilaSiembraEmbudo fila = recibidas[i];
                string cliente = fila?.Cliente?.Trim();
                if (string.IsNullOrEmpty(cliente) || cliente.Length > 10)
                {
                    throw new PeticionEmbudoNoValidaException($"Fila {i + 1}: falta el Nº de cliente o es demasiado largo.");
                }
                if (!fila.Fecha.HasValue || fila.Fecha.Value.Year < 2000)
                {
                    throw new PeticionEmbudoNoValidaException($"Fila {i + 1} (cliente {cliente}): falta la fecha.");
                }
                string origen = fila.Origen?.Trim();
                if (string.IsNullOrEmpty(origen) || origen.Length > LONGITUD_MAXIMA_ORIGEN)
                {
                    throw new PeticionEmbudoNoValidaException($"Fila {i + 1} (cliente {cliente}): falta el origen o tiene más de {LONGITUD_MAXIMA_ORIGEN} caracteres.");
                }
            }
            // El mismo cliente dos veces: vale la primera (el registro es de una vez por cliente)
            List<FilaSiembraEmbudo> filas = recibidas
                .Select(f => new FilaSiembraEmbudo { Cliente = f.Cliente.Trim(), Fecha = f.Fecha, Origen = f.Origen.Trim() })
                .GroupBy(f => f.Cliente, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            bool ensayo = peticion.Ensayo ?? true;

            if (!await repositorio.HayTabla().ConfigureAwait(false))
            {
                throw new PeticionEmbudoNoValidaException(MensajeSinTabla);
            }

            List<EstadoSiembraEmbudo> estados = await repositorio.EstadoSiembra(filas.Select(f => f.Cliente).ToList()).ConfigureAwait(false)
                ?? new List<EstadoSiembraEmbudo>();
            Dictionary<string, EstadoSiembraEmbudo> porCliente = estados
                .Where(e => e?.Cliente != null)
                .GroupBy(e => e.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            EstadoSiembraEmbudo Estado(string cliente) => porCliente.TryGetValue(cliente, out EstadoSiembraEmbudo e) ? e : new EstadoSiembraEmbudo { Cliente = cliente };

            List<FilaSiembraEmbudo> nuevas = filas.Where(f => Estado(f.Cliente).Existe && !Estado(f.Cliente).YaEstaba).ToList();
            var resultado = new ResultadoSembrarEmbudo
            {
                Ensayo = ensayo,
                Recibidas = recibidas.Count,
                YaEstaban = filas.Count(f => Estado(f.Cliente).Existe && Estado(f.Cliente).YaEstaba),
                NoExisten = filas.Where(f => !Estado(f.Cliente).Existe).Select(f => f.Cliente).ToList(),
                Nuevas = nuevas.Count
            };
            if (!ensayo && nuevas.Count > 0)
            {
                resultado.Nuevas = await repositorio.Sembrar(nuevas, usuario).ConfigureAwait(false);
            }
            return resultado;
        }
    }
}
