using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>NestoAPI#577: una fila del calendario de reposiciones (tabla ReposicionesCalendario).</summary>
    public class ReposicionCalendarioDTO
    {
        /// <summary>0 o null al crear una fila nueva.</summary>
        public int? Id { get; set; }
        public string Empresa { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>1 lunes … 7 domingo: el día de LLEGADA (el de la ruta).</summary>
        public byte DiaSemana { get; set; }
        /// <summary>"10:00:00": a esa hora se rellena sola la reposición y la tienda la prepara.</summary>
        public TimeSpan HoraCierre { get; set; }
        /// <summary>"13:30:00": a qué hora suele entrar en el destino.</summary>
        public TimeSpan HoraLlegadaHabitual { get; set; }
        /// <summary>
        /// NestoAPI#577 (corte 3d): cuántos laborables del origen antes del día de llegada se cierra, a HoraCierre. 0 = el
        /// mismo día (tienda → Algete); 1 = el laborable anterior (Algete → tienda: la del lunes cierra el viernes).
        /// </summary>
        public byte LaborablesAntelacionCierre { get; set; }
        public bool Activo { get; set; } = true;
        /// <summary>Solo lectura (la API pone el del Identity).</summary>
        public string Usuario { get; set; }
        /// <summary>Solo lectura.</summary>
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#577: PUT api/Reposiciones/Calendario. Con <see cref="Origen"/> y <see cref="Destino"/> es la lista COMPLETA de
    /// esa ruta: las filas de la ruta que no vengan se DESACTIVAN (nunca se borran). Sin ellos son filas sueltas: se crean o
    /// se cambian y no se toca nada más. Cada fila se busca por Id y, si no trae, por (empresa, origen, destino, día de
    /// llegada): «añadir» un día que ya existe desactivado lo vuelve a activar con lo que venga.
    /// </summary>
    public class GuardarCalendarioReposicionesDTO
    {
        public string Empresa { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public List<ReposicionCalendarioDTO> Filas { get; set; }
    }

    public interface IServicioCalendarioReposiciones
    {
        Task<ProximaReposicionDTO> LeerProximaLlegada(string empresa, string origen, string destino);
        Task<List<ReposicionCalendarioDTO>> LeerCalendario(string empresa);
        Task<List<ReposicionCalendarioDTO>> Guardar(GuardarCalendarioReposicionesDTO peticion, string usuario);
    }

    /// <summary>
    /// NestoAPI#577 (corte 1): lee el calendario, los festivos y la hora de corte del picking y se lo pasa a
    /// <see cref="CalculadoraFechaReposicion"/>; y mantiene el calendario. Desde el 09/10/26 lo mantienen desde Nesto las
    /// mismas personas que pueden rellenar reposiciones a mano (<see cref="PermisoRellenarReposicionManual"/>; el
    /// controlador lo comprueba).
    ///
    /// <para>Reglas de una ruta (además de las de cada fila): una sola fila por día de llegada (activa o no) y todas sus
    /// filas activas con la misma antelación de cierre. Con eso, el día de cierre identifica el viaje, que es lo que mira
    /// el job para no repetir una reposición cuando se cambia la hora de cierre el mismo día
    /// (<see cref="ServicioReposicionAutomatica"/>, «día ya tratado»).</para>
    /// </summary>
    public class ServicioCalendarioReposiciones : IServicioCalendarioReposiciones
    {
        public const string MENSAJE_SIN_PERMISO =
            "El calendario de reposiciones solo lo pueden cambiar las personas autorizadas a rellenar reposiciones a mano.";

        private static readonly string[] nombresDias = { "lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo" };

        /// <summary>NestoAPI#577 (corte 3d): como el CHECK de la columna LaborablesAntelacionCierre.</summary>
        public const byte MAXIMO_LABORABLES_ANTELACION = 5;

        private static readonly string[] gruposQuePuedenMantener =
        {
            Constantes.GruposSeguridad.ALMACEN,
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA
        };

        private readonly NVEntities db;
        private readonly CalculadoraFechaReposicion calculadora;
        private readonly Func<DateTime> reloj;

        public ServicioCalendarioReposiciones(NVEntities db) : this(db, new CalculadoraFechaReposicion(), () => DateTime.Now)
        {
        }

        internal ServicioCalendarioReposiciones(NVEntities db, CalculadoraFechaReposicion calculadora, Func<DateTime> reloj)
        {
            this.db = db;
            this.calculadora = calculadora;
            this.reloj = reloj;
        }

        /// <summary>Relanzar la reposición automática (POST RellenarAutomatica): Almacén, Dirección e Informática.</summary>
        public static bool PuedeMantener(IPrincipal usuario)
        {
            return usuario != null && gruposQuePuedenMantener.Any(g => usuario.IsInRoleSinDominio(g));
        }

        public async Task<ProximaReposicionDTO> LeerProximaLlegada(string empresa, string origen, string destino)
        {
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            string origenLimpio = Almacen(origen, "origen");
            string destinoLimpio = Almacen(destino, "destino");

            List<ReposicionCalendario> filas = await db.ReposicionesCalendario
                .Where(f => f.Empresa == empresaLimpia && f.AlmacenOrigen == origenLimpio && f.AlmacenDestino == destinoLimpio && f.Activo)
                .ToListAsync().ConfigureAwait(false);
            ParametroUsuario parametro = await db.ParametrosUsuario
                .FirstOrDefaultAsync(p => p.Empresa == empresaLimpia && p.Usuario == "(defecto)" && p.Clave == HoraCortePicking.CLAVE)
                .ConfigureAwait(false);

            return calculadora.Calcular(filas, origenLimpio, destinoLimpio, HoraCortePicking.Interpretar(parametro?.Valor), reloj(), empresaLimpia);
        }

        public async Task<List<ReposicionCalendarioDTO>> LeerCalendario(string empresa)
        {
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            List<ReposicionCalendario> filas = await db.ReposicionesCalendario
                .Where(f => f.Empresa == empresaLimpia)
                .ToListAsync().ConfigureAwait(false);
            return filas
                .OrderBy(f => f.AlmacenOrigen).ThenBy(f => f.AlmacenDestino).ThenBy(f => f.DiaSemana).ThenBy(f => f.HoraCierre)
                .Select(ADTO)
                .ToList();
        }

        public async Task<List<ReposicionCalendarioDTO>> Guardar(GuardarCalendarioReposicionesDTO peticion, string usuario)
        {
            if (peticion?.Filas == null)
            {
                throw new NestoBusinessException("Faltan las filas del calendario.");
            }
            string empresa = EmpresaOPorDefecto(peticion.Empresa);
            bool rutaCompleta = !string.IsNullOrWhiteSpace(peticion.Origen) || !string.IsNullOrWhiteSpace(peticion.Destino);
            string origenRuta = rutaCompleta ? Almacen(peticion.Origen, "origen") : null;
            string destinoRuta = rutaCompleta ? Almacen(peticion.Destino, "destino") : null;

            List<ReposicionCalendarioDTO> filas = peticion.Filas.Where(f => f != null).Select(f => Validar(f, origenRuta, destinoRuta)).ToList();
            IGrouping<string, ReposicionCalendarioDTO> repetida = filas.GroupBy(Clave).FirstOrDefault(g => g.Count() > 1);
            if (repetida != null)
            {
                throw new NestoBusinessException($"La reposición {repetida.Key} viene repetida.");
            }

            List<ReposicionCalendario> existentes = await db.ReposicionesCalendario
                .Where(f => f.Empresa == empresa)
                .ToListAsync().ConfigureAwait(false);
            DateTime ahora = reloj();
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(SinDominio(usuario));
            var tocadas = new HashSet<ReposicionCalendario>();
            var nuevas = new List<ReposicionCalendario>();

            foreach (ReposicionCalendarioDTO fila in filas)
            {
                ReposicionCalendario entidad;
                if (fila.Id.GetValueOrDefault() > 0)
                {
                    entidad = existentes.SingleOrDefault(e => e.Id == fila.Id.Value)
                        ?? throw new NestoBusinessException($"No existe la fila {fila.Id} del calendario de reposiciones.");
                }
                else
                {
                    entidad = existentes.FirstOrDefault(e => Clave(e) == Clave(fila));
                }
                if (entidad == null)
                {
                    entidad = new ReposicionCalendario { Empresa = empresa };
                    nuevas.Add(entidad);
                }
                entidad.AlmacenOrigen = fila.Origen;
                entidad.AlmacenDestino = fila.Destino;
                entidad.DiaSemana = fila.DiaSemana;
                entidad.HoraCierre = fila.HoraCierre;
                entidad.HoraLlegadaHabitual = fila.HoraLlegadaHabitual;
                entidad.LaborablesAntelacionCierre = fila.LaborablesAntelacionCierre;
                entidad.Activo = fila.Activo;
                entidad.Usuario = usuarioAuditoria;
                entidad.FechaModificacion = ahora;
                tocadas.Add(entidad);
            }

            if (rutaCompleta)
            {
                List<ReposicionCalendario> sobrantes = existentes
                    .Where(e => !tocadas.Contains(e) && e.Activo && Limpiar(e.AlmacenOrigen) == origenRuta && Limpiar(e.AlmacenDestino) == destinoRuta)
                    .ToList();
                foreach (ReposicionCalendario sobrante in sobrantes)
                {
                    sobrante.Activo = false;
                    sobrante.Usuario = usuarioAuditoria;
                    sobrante.FechaModificacion = ahora;
                }
            }

            ComprobarRutas(existentes.Concat(nuevas).ToList());
            foreach (ReposicionCalendario nueva in nuevas)
            {
                db.ReposicionesCalendario.Add(nueva);
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
            return await LeerCalendario(empresa).ConfigureAwait(false);
        }

        /// <summary>
        /// Las reglas que miran la ruta entera, sobre cómo quedaría el calendario: una fila por día de llegada y la misma
        /// antelación en todas las activas.
        /// </summary>
        private static void ComprobarRutas(List<ReposicionCalendario> calendario)
        {
            foreach (var ruta in calendario.GroupBy(e => new { Origen = Limpiar(e.AlmacenOrigen), Destino = Limpiar(e.AlmacenDestino) }))
            {
                IGrouping<byte, ReposicionCalendario> repetido = ruta.GroupBy(e => e.DiaSemana).FirstOrDefault(g => g.Count() > 1);
                if (repetido != null)
                {
                    bool hayDesactivada = repetido.Any(e => !e.Activo);
                    throw new NestoBusinessException($"Ya hay una reposición de {ruta.Key.Origen} a {ruta.Key.Destino} que llega el {NombreDia(repetido.Key)}" +
                        (hayDesactivada ? " (está desactivada: actívala en vez de crear otra)." : "."));
                }
                List<byte> antelaciones = ruta.Where(e => e.Activo).Select(e => e.LaborablesAntelacionCierre).Distinct().ToList();
                if (antelaciones.Count > 1)
                {
                    throw new NestoBusinessException($"Todos los días de la reposición de {ruta.Key.Origen} a {ruta.Key.Destino} se tienen que cerrar con " +
                        "la misma antelación (el mismo día, o los mismos laborables antes). Cámbiala en todos a la vez.");
                }
            }
        }

        internal static string NombreDia(byte diaSemana) => diaSemana >= 1 && diaSemana <= 7 ? nombresDias[diaSemana - 1] : diaSemana.ToString(CultureInfo.InvariantCulture);

        private static string SinDominio(string usuario) => usuario?.Substring(usuario.LastIndexOf('\\') + 1);

        private static ReposicionCalendarioDTO Validar(ReposicionCalendarioDTO fila, string origenRuta, string destinoRuta)
        {
            string origen = string.IsNullOrWhiteSpace(fila.Origen) && origenRuta != null ? origenRuta : Almacen(fila.Origen, "origen");
            string destino = string.IsNullOrWhiteSpace(fila.Destino) && destinoRuta != null ? destinoRuta : Almacen(fila.Destino, "destino");
            if (origenRuta != null && (origen != origenRuta || destino != destinoRuta))
            {
                throw new NestoBusinessException($"La fila {origen}→{destino} no es de la ruta {origenRuta}→{destinoRuta}.");
            }
            if (!ServicioPreparacionReposicion.ALMACENES_REPOSICION.Contains(origen) || !ServicioPreparacionReposicion.ALMACENES_REPOSICION.Contains(destino))
            {
                throw new NestoBusinessException($"Los almacenes de una reposición tienen que ser {string.Join(", ", ServicioPreparacionReposicion.ALMACENES_REPOSICION)}.");
            }
            if (origen == destino)
            {
                throw new NestoBusinessException("El almacén de origen y el de destino no pueden ser el mismo.");
            }
            if (fila.DiaSemana < 1 || fila.DiaSemana > 7)
            {
                throw new NestoBusinessException("El día de la semana va del 1 (lunes) al 7 (domingo).");
            }
            if (!EsHoraDelDia(fila.HoraCierre) || !EsHoraDelDia(fila.HoraLlegadaHabitual))
            {
                throw new NestoBusinessException("Las horas tienen que estar entre las 00:00 y las 23:59.");
            }
            if (fila.LaborablesAntelacionCierre > MAXIMO_LABORABLES_ANTELACION)
            {
                throw new NestoBusinessException($"La reposición no se puede cerrar más de {MAXIMO_LABORABLES_ANTELACION} laborables antes de que llegue.");
            }
            if (fila.LaborablesAntelacionCierre == 0 && fila.HoraLlegadaHabitual < fila.HoraCierre)
            {
                throw new NestoBusinessException("La hora de llegada no puede ser anterior a la hora de cierre (la reposición llega el mismo día). " +
                    "Si se cierra el día antes, pon los laborables de antelación.");
            }
            return new ReposicionCalendarioDTO
            {
                Id = fila.Id,
                Origen = origen,
                Destino = destino,
                DiaSemana = fila.DiaSemana,
                HoraCierre = new TimeSpan(fila.HoraCierre.Hours, fila.HoraCierre.Minutes, 0),
                HoraLlegadaHabitual = new TimeSpan(fila.HoraLlegadaHabitual.Hours, fila.HoraLlegadaHabitual.Minutes, 0),
                LaborablesAntelacionCierre = fila.LaborablesAntelacionCierre,
                Activo = fila.Activo
            };
        }

        private static bool EsHoraDelDia(TimeSpan hora) => hora >= TimeSpan.Zero && hora < TimeSpan.FromDays(1);

        private static string Clave(ReposicionCalendarioDTO f) => Clave(f.Origen, f.Destino, f.DiaSemana);

        private static string Clave(ReposicionCalendario f) => Clave(Limpiar(f.AlmacenOrigen), Limpiar(f.AlmacenDestino), f.DiaSemana);

        private static string Clave(string origen, string destino, byte dia) =>
            string.Format(CultureInfo.InvariantCulture, "de {0} a {1} que llega el {2}", origen, destino, NombreDia(dia));

        private static ReposicionCalendarioDTO ADTO(ReposicionCalendario f)
        {
            return new ReposicionCalendarioDTO
            {
                Id = f.Id,
                Empresa = Limpiar(f.Empresa),
                Origen = Limpiar(f.AlmacenOrigen),
                Destino = Limpiar(f.AlmacenDestino),
                DiaSemana = f.DiaSemana,
                HoraCierre = f.HoraCierre,
                HoraLlegadaHabitual = f.HoraLlegadaHabitual,
                LaborablesAntelacionCierre = f.LaborablesAntelacionCierre,
                Activo = f.Activo,
                Usuario = f.Usuario?.Trim(),
                FechaModificacion = f.FechaModificacion
            };
        }

        private static string EmpresaOPorDefecto(string empresa) => Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private static string Almacen(string almacen, string nombre)
        {
            string limpio = Limpiar(almacen);
            if (limpio == null || limpio.Length > 3)
            {
                throw new NestoBusinessException($"Falta el almacén de {nombre} (o no es válido).");
            }
            return limpio;
        }

        private static string Limpiar(string valor)
        {
            string limpio = valor?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }
}
