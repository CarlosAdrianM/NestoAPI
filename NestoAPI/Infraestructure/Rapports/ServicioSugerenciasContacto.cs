using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    public interface IServicioSugerenciasContacto
    {
        Task<SugerenciasContactoDTO> Leer(string vendedor, string tipoInteraccion, int numero, string grupoSubgrupo, string usuario);
        Task<List<UsoSugerenciasContactoDTO>> LeerUso(DateTime desde, DateTime hasta);
    }

    /// <summary>
    /// NestoAPI#603 (corte 1): junta la cartera (<see cref="IRepositorioCarteraContacto"/>), la probabilidad del modelo
    /// (<see cref="IProbabilidadesContacto"/>), el motor (<see cref="MotorSugerenciasContacto"/>) y el registro del día
    /// (<see cref="RegistroSugerenciasContacto"/>).
    /// <para>La primera consulta del día de un vendedor fija su lista: se guardan las <c>numero</c> primeras y las consultas
    /// siguientes devuelven esas mismas (con Atendida al día y los datos del cliente frescos). Si piden más de las que
    /// hay, se añaden las siguientes de la lista viva que no estuvieran ya. El ritmo y los pendientes se calculan siempre
    /// en vivo.</para>
    /// </summary>
    public class ServicioSugerenciasContacto : IServicioSugerenciasContacto
    {
        public const int NUMERO_MAXIMO = 200;

        private readonly NVEntities db;
        private readonly IRepositorioCarteraContacto repositorio;
        private readonly IProbabilidadesContacto probabilidades;
        private readonly MotorSugerenciasContacto motor;
        private readonly Func<DateTime> reloj;
        private readonly Func<DateTime, string, bool> esFestivo;

        public ServicioSugerenciasContacto(NVEntities db)
            : this(db, new RepositorioCarteraContactoSql(), new ProbabilidadesContactoModelo(), () => DateTime.Now, GestorFestivos.EsFestivo)
        {
        }

        internal ServicioSugerenciasContacto(NVEntities db, IRepositorioCarteraContacto repositorio, IProbabilidadesContacto probabilidades,
            Func<DateTime> reloj, Func<DateTime, string, bool> esFestivo)
        {
            this.db = db;
            this.repositorio = repositorio;
            this.probabilidades = probabilidades;
            this.reloj = reloj;
            this.esFestivo = esFestivo;
            motor = new MotorSugerenciasContacto();
        }

        public async Task<SugerenciasContactoDTO> Leer(string vendedor, string tipoInteraccion, int numero, string grupoSubgrupo, string usuario)
        {
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                throw new ArgumentException("Hay que indicar el vendedor.", nameof(vendedor));
            }
            string vendedorLimpio = vendedor.Trim().ToUpperInvariant();
            numero = Math.Max(1, Math.Min(NUMERO_MAXIMO, numero));
            DateTime ahora = reloj();
            DateTime hoy = ahora.Date;

            List<ClienteCarteraContacto> cartera = await repositorio.LeerCartera(vendedorLimpio, hoy).ConfigureAwait(false);
            Dictionary<string, PrediccionContacto> predicciones = await probabilidades.Leer(vendedorLimpio, tipoInteraccion, grupoSubgrupo).ConfigureAwait(false)
                ?? new Dictionary<string, PrediccionContacto>();
            foreach (ClienteCarteraContacto cliente in cartera)
            {
                if (predicciones.TryGetValue(cliente.Clave, out PrediccionContacto prediccion))
                {
                    cliente.Probabilidad = prediccion.Probabilidad;
                    cliente.GrupoSubgrupoMasVendido = prediccion.GrupoSubgrupoMasVendido;
                }
            }

            List<SugerenciaContactoDTO> pendientes = motor.Priorizar(cartera, hoy);
            ContactosVendedor contactos = await repositorio.LeerContactos(vendedorLimpio, hoy).ConfigureAwait(false);
            string delegacion = await repositorio.LeerDelegacion(vendedorLimpio).ConfigureAwait(false) ?? Constantes.Almacenes.ALGETE;
            RitmoContactosDTO ritmo = motor.CalcularRitmo(cartera, contactos, pendientes, hoy, d => !esFestivo(d, delegacion));

            var registro = new RegistroSugerenciasContacto(db);
            List<SugerenciaContacto> delDia = await registro.LeerDelDia(vendedorLimpio, hoy).ConfigureAwait(false);
            bool cambios = await registro.ConciliarAtendidas(delDia, hoy).ConfigureAwait(false);
            if (delDia.Count < numero)
            {
                var yaSugeridos = new HashSet<string>(delDia.Select(s => ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto)), StringComparer.OrdinalIgnoreCase);
                List<SugerenciaContactoDTO> nuevas = pendientes
                    .Where(p => !yaSugeridos.Contains(ClienteCarteraContacto.ClaveDe(p.Cliente, p.Contacto)))
                    .Take(numero - delDia.Count)
                    .ToList();
                if (nuevas.Any())
                {
                    int ultimoOrden = delDia.Any() ? delDia.Max(s => s.Orden) : 0;
                    delDia.AddRange(registro.Anadir(nuevas, ultimoOrden, vendedorLimpio, usuario, ahora));
                    cambios = true;
                }
            }
            if (cambios)
            {
                _ = await registro.Guardar().ConfigureAwait(false);
            }

            Dictionary<string, ClienteCarteraContacto> porClave = cartera
                .GroupBy(c => c.Clave, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            return new SugerenciasContactoDTO
            {
                Vendedor = vendedorLimpio,
                Fecha = ahora,
                Ritmo = ritmo,
                Sugerencias = delDia
                    .OrderBy(s => s.Orden)
                    .Take(numero)
                    .Select(s => ADTO(s, porClave, hoy))
                    .ToList()
            };
        }

        private static SugerenciaContactoDTO ADTO(SugerenciaContacto fila, Dictionary<string, ClienteCarteraContacto> cartera, DateTime hoy)
        {
            SugerenciaContactoDTO dto = cartera.TryGetValue(ClienteCarteraContacto.ClaveDe(fila.Cliente, fila.Contacto), out ClienteCarteraContacto cliente)
                ? MotorSugerenciasContacto.ADTO(cliente, hoy)
                : new SugerenciaContactoDTO { Cliente = fila.Cliente?.Trim(), Contacto = fila.Contacto?.Trim(), DiasDesdeUltimoPedido = 9999 };
            dto.SugerenciaId = fila.Id;
            dto.Prioridad = fila.Prioridad?.Trim();
            dto.Orden = fila.Orden;
            dto.Motivo = fila.Motivo;
            dto.Probabilidad = fila.Probabilidad;
            dto.Atendida = fila.Atendida;
            return dto;
        }

        public async Task<List<UsoSugerenciasContactoDTO>> LeerUso(DateTime desde, DateTime hasta)
        {
            DateTime inicio = desde.Date;
            DateTime fin = hasta.Date.AddDays(1);
            var filas = await db.SugerenciasContacto
                .Where(s => s.Fecha >= inicio && s.Fecha < fin)
                .Select(s => new { s.Vendedor, s.Fecha, s.Atendida })
                .ToListAsync().ConfigureAwait(false);
            if (!filas.Any())
            {
                return new List<UsoSugerenciasContactoDTO>();
            }
            List<string> vendedores = filas.Select(f => f.Vendedor.Trim()).Distinct().ToList();
            var rapports = await db.SeguimientosClientes
                .Where(r => r.Fecha >= inicio && r.Fecha < fin && (r.Estado == 0 || r.Estado == 1) && vendedores.Contains(r.Vendedor))
                .GroupBy(r => r.Vendedor)
                .Select(g => new { Vendedor = g.Key, Total = g.Count() })
                .ToListAsync().ConfigureAwait(false);
            Dictionary<string, int> rapportsPorVendedor = rapports
                .GroupBy(r => r.Vendedor.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Total), StringComparer.OrdinalIgnoreCase);

            return filas
                .GroupBy(f => f.Vendedor.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    int sugeridas = g.Count();
                    int atendidas = g.Count(f => f.Atendida);
                    return new UsoSugerenciasContactoDTO
                    {
                        Vendedor = g.Key,
                        DiasConSugerencias = g.Select(f => f.Fecha.Date).Distinct().Count(),
                        Sugeridas = sugeridas,
                        Atendidas = atendidas,
                        PorcentajeAtendidas = sugeridas == 0 ? 0 : Math.Round((double)atendidas / sugeridas, 4),
                        RapportsTotales = rapportsPorVendedor.TryGetValue(g.Key, out int total) ? total : 0
                    };
                })
                .OrderBy(u => u.Vendedor)
                .ToList();
        }
    }
}
