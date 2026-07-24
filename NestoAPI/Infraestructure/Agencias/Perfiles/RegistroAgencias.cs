using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Infraestructure.Agencias.Perfiles
{
    /// <summary>
    /// NestoAPI#258: registro de los perfiles de agencia ACTIVOS. Descubre por reflexión todas las
    /// clases que implementan <see cref="IPerfilAgencia"/> en el ensamblado de NestoAPI y se queda
    /// con las que <see cref="IGateAgenciasActivas"/> marca como activas. El resto del sistema
    /// (fábrica remota, comparador, controller de envíos) consultará aquí por AgenciaId o por
    /// capacidad, en vez de con switch/if.
    ///
    /// Fase 1 de #258: solo descubrimiento + puerta (sin cambiar comportamiento). El consumo desde
    /// la fábrica y el controller se cablea en fases posteriores.
    /// </summary>
    public class RegistroAgencias
    {
        private readonly IReadOnlyList<IPerfilAgencia> _activos;

        public RegistroAgencias(IEnumerable<IPerfilAgencia> perfiles, IGateAgenciasActivas gate)
        {
            List<IPerfilAgencia> todos = perfiles?.ToList() ?? new List<IPerfilAgencia>();
            VerificarUnicoPorAgencia(todos);
            _activos = todos.Where(p => gate.EstaActiva(p.AgenciaId)).ToList();
        }

        /// <summary>Construye el registro descubriendo los perfiles por reflexión del ensamblado de NestoAPI.</summary>
        public static RegistroAgencias PorReflexion(IGateAgenciasActivas gate)
            => new RegistroAgencias(DescubrirPerfiles(typeof(RegistroAgencias).Assembly), gate);

        /// <summary>Perfil activo de esa agencia, o null si no existe clase o está inactiva.</summary>
        public IPerfilAgencia Perfil(int agenciaId) => _activos.FirstOrDefault(p => p.AgenciaId == agenciaId);

        public IReadOnlyCollection<IPerfilAgencia> Perfiles => _activos;

        /// <summary>
        /// Ids de las agencias activas que tienen la capacidad <typeparamref name="TCapacidad"/> (una
        /// de las sub-interfaces IPerfilCon...). Sustituye a los arrays hardcodeados tipo
        /// _conGestionRemota / _conSeguimiento de FabricaAgenciasRemotas.
        /// </summary>
        public IReadOnlyCollection<int> ConCapacidad<TCapacidad>() where TCapacidad : class, IPerfilAgencia
            => _activos.OfType<TCapacidad>().Select(p => p.AgenciaId).ToList();

        internal static IEnumerable<IPerfilAgencia> DescubrirPerfiles(Assembly ensamblado)
        {
            return ObtenerTipos(ensamblado)
                .Where(t => typeof(IPerfilAgencia).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                .Select(t => (IPerfilAgencia)Activator.CreateInstance(t))
                .ToList();
        }

        private static IEnumerable<Type> ObtenerTipos(Assembly ensamblado)
        {
            try
            {
                return ensamblado.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Si algún tipo del ensamblado no carga, seguimos con los que sí (no dejamos caer
                // todo el registro por una dependencia rota ajena a los perfiles).
                return ex.Types.Where(t => t != null);
            }
        }

        private static void VerificarUnicoPorAgencia(IEnumerable<IPerfilAgencia> perfiles)
        {
            IGrouping<int, IPerfilAgencia> duplicado = perfiles
                .GroupBy(p => p.AgenciaId)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicado != null)
            {
                string clases = string.Join(", ", duplicado.Select(p => p.GetType().Name));
                throw new InvalidOperationException(
                    $"Hay más de un perfil para la agencia {duplicado.Key}: {clases}. " +
                    "Debe haber una sola clase de perfil por agencia (NestoAPI#258).");
            }
        }
    }
}
