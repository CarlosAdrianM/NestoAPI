using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.RecursosHumanos
{
    public class GestorFestivos : IGestorDiasNoLaborables
    {
        public GestorFestivos()
        {
            ListaFestivos = new List<Festivo>();
        }
        public List<Festivo> ListaFestivos { get; set; }
        public void Rellenar(DateTime fechaHasta, DateTime fechaDesde)
        {
            Festivo festivo1 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 1),
                Fiesta = "Fiesta del Trabajo",
                TipoFestivo = TipoFestivo.Nacional
            };
            ListaFestivos.Add(festivo1);
            Festivo festivo2 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 2),
                Fiesta = "Fiesta de la Comunidad de Madrid",
                TipoFestivo = TipoFestivo.Nacional
            };
            ListaFestivos.Add(festivo2);
            Festivo festivo3 = new Festivo()
            {
                Fecha = new DateTime(2017, 8, 15),
                Fiesta = "Asunción de la Virgen",
                TipoFestivo = TipoFestivo.Nacional
            };
            ListaFestivos.Add(festivo3);
            Festivo festivo4 = new Festivo()
            {
                Fecha = new DateTime(2017, 10, 12),
                Fiesta = "Fiesta Nacional",
                TipoFestivo = TipoFestivo.Nacional
            };
            ListaFestivos.Add(festivo4);
            Festivo festivo5 = new Festivo()
            {
                Fecha = new DateTime(2017, 11, 1),
                Fiesta = "Todos los Santos",
                TipoFestivo = TipoFestivo.Nacional
            };
            ListaFestivos.Add(festivo5);
            Festivo festivo6 = new Festivo()
            {
                Fecha = new DateTime(2017, 12, 6),
                Fiesta = "Día de la Constitución",
                TipoFestivo = TipoFestivo.Nacional
            };
            ListaFestivos.Add(festivo6);
            Festivo festivo7 = new Festivo()
            {
                Fecha = new DateTime(2017, 12, 8),
                Fiesta = "Día de la Inmaculada Concepción",
                TipoFestivo = TipoFestivo.Nacional
            };
            ListaFestivos.Add(festivo7);
            Festivo festivo8 = new Festivo()
            {
                Fecha = new DateTime(2017, 12, 25),
                Fiesta = "Natividad del Señor",
                TipoFestivo = TipoFestivo.Nacional
            };
            ListaFestivos.Add(festivo8);
            /*
            // ALGETE
            Festivo festivo9 = new Festivo()
            {
                Fecha = new DateTime(2017, 9, 8),
                Fiesta = "Fiesta local Algete",
                TipoFestivo = TipoFestivo.Algete
            };
            ListaFestivos.Add(festivo9);
            Festivo festivo10 = new Festivo()
            {
                Fecha = new DateTime(2017, 9, 11),
                Fiesta = "Fiesta local Algete",
                TipoFestivo = TipoFestivo.Algete
            };
            ListaFestivos.Add(festivo10);
            */

            //MADRID
            Festivo festivo11 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 15),
                Fiesta = "Fiesta local Madrid (San Isidro)",
                TipoFestivo = TipoFestivo.Madrid
            };
            ListaFestivos.Add(festivo11);
            Festivo festivo12 = new Festivo()
            {
                Fecha = new DateTime(2017, 11, 9),
                Fiesta = "Fiesta local Madrid (Almudena)",
                TipoFestivo = TipoFestivo.Madrid
            };
            ListaFestivos.Add(festivo12);
        }

        public static bool EsFestivo(DateTime fecha, string delegacion)
        {
            var fechaSinHora = new DateTime(fecha.Year, fecha.Month, fecha.Day);

            // Los sábados y los domingos no trabajamos
            if (fechaSinHora.DayOfWeek == DayOfWeek.Saturday || fechaSinHora.DayOfWeek == DayOfWeek.Sunday)
            {
                return true;
            }

            // NestoAPI#779: los festivos del año salen de la tabla Festivos (CalendarioFestivos, caché de 10 min) o, si no
            // hay tabla (o no tiene ese año), de la lista de este fichero (FiestasDelCodigo), como hasta ahora.
            IReadOnlyList<Festivo> fiestas = CalendarioFestivos.DelAnio(fechaSinHora.Year);

            // Buscar en la lista Fiestas si hay algún elemento que coincida con la fecha y el tipo de festivo
            // NestoAPI#777: las autonómicas (Jueves Santo, 2 de mayo, traslados de la Comunidad de Madrid) cuentan en todas
            // las delegaciones (Algete, Alcobendas y Reina están en la Comunidad de Madrid) y también sin delegación.
            var festivoEncontrado = fiestas.Any(f => f.Fecha == fechaSinHora && (f.TipoFestivo == TipoFestivo.Nacional || f.TipoFestivo == TipoFestivo.Autonómica));
            if (festivoEncontrado)
            {
                return true;
            }

            // Verificar fiestas según el valor del parámetro "delegacion"
            switch (delegacion)
            {
                case Constantes.Almacenes.ALGETE:
                    return fiestas.Any(f => f.Fecha == fechaSinHora && (f.TipoFestivo == TipoFestivo.Empresa));
                case Constantes.Almacenes.ALCOBENDAS:
                    return fiestas.Any(f => f.Fecha == fechaSinHora && (f.TipoFestivo == TipoFestivo.Alcobendas || f.TipoFestivo == TipoFestivo.Empresa));
                case Constantes.Almacenes.REINA:
                    return fiestas.Any(f => f.Fecha == fechaSinHora && (f.TipoFestivo == TipoFestivo.Madrid || f.TipoFestivo == TipoFestivo.Empresa));
                case REPARTO_PROPIO:
                    // NestoAPI#841: la furgoneta sale de Algete: los festivos de Algete y, además, los días sin reparto propio
                    return fiestas.Any(f => f.Fecha == fechaSinHora && (f.TipoFestivo == TipoFestivo.Empresa || f.TipoFestivo == TipoFestivo.RepartoPropio));
                default:
                    return false;
            }
        }

        /// <summary>
        /// NestoAPI#841: la «delegación» del reparto propio (la furgoneta) para las funciones (día, almacén) → festivo:
        /// <c>EsFestivo(día, REPARTO_PROPIO)</c> = festivo de Algete (de donde sale) o día sin reparto propio. No es un almacén:
        /// los almacenes y las tiendas siguen con sus festivos (un día <see cref="TipoFestivo.RepartoPropio"/> no lo es de ninguno).
        /// </summary>
        public const string REPARTO_PROPIO = "RepartoPropio";

        /// <summary>
        /// NestoAPI#841: ¿ese día hay una fila <see cref="TipoFestivo.RepartoPropio"/> (no sale la furgoneta)? Solo eso: ni fines de
        /// semana ni festivos (para «no se reparte por nuestra ruta», <see cref="EsFestivoRepartoPropio"/>).
        /// </summary>
        public static bool EsDiaSinRepartoPropio(DateTime fecha)
        {
            DateTime dia = fecha.Date;
            return CalendarioFestivos.DelAnio(dia.Year).Any(f => f.Fecha.Date == dia && f.TipoFestivo == TipoFestivo.RepartoPropio);
        }

        /// <summary>
        /// NestoAPI#841: ¿ese día NO se reparte por nuestra ruta (AT/16, agencia 14)? Sábado, domingo, festivo de Algete o día sin
        /// reparto propio. LA REGLA de los días del reparto propio (fecha de entrega, picking de rutas internas, etiqueta RP, furgoneta).
        /// </summary>
        public static bool EsFestivoRepartoPropio(DateTime fecha) => EsFestivo(fecha, REPARTO_PROPIO);

        /// <summary>
        /// NestoAPI#841: lo mismo que <see cref="EsFestivoRepartoPropio"/> con una función (día, almacén) inyectada: festivo de Algete
        /// o de <see cref="REPARTO_PROPIO"/>. Así las funciones de prueba que solo saben de almacenes siguen valiendo.
        /// </summary>
        public static bool EsFestivoRepartoPropio(DateTime fecha, Func<DateTime, string, bool> esFestivo)
        {
            if (esFestivo == null)
            {
                throw new ArgumentNullException(nameof(esFestivo));
            }
            return esFestivo(fecha, Constantes.Almacenes.ALGETE) || esFestivo(fecha, REPARTO_PROPIO);
        }

        /// <summary>
        /// NestoAPI#627: el propio día si la delegación trabaja ese día; si no (sábado, domingo o festivo), el primer laborable
        /// posterior. P. ej., una recepción prevista de proveedor en sábado cuenta como el lunes (ese día no se puede dar de alta
        /// la mercancía).
        /// </summary>
        public static DateTime PrimerLaborableDesde(DateTime fecha, string delegacion)
        {
            return PrimerLaborableDesde(fecha, delegacion, EsFestivo);
        }

        /// <param name="esFestivo">(día, delegación) → true si esa delegación no trabaja ese día. Los sábados y domingos nunca
        /// son laborables, lo diga o no esta función.</param>
        public static DateTime PrimerLaborableDesde(DateTime fecha, string delegacion, Func<DateTime, string, bool> esFestivo)
        {
            if (esFestivo == null)
            {
                throw new ArgumentNullException(nameof(esFestivo));
            }
            DateTime dia = fecha.Date;
            for (int i = 0; i < DIAS_MAXIMOS_BUSQUEDA_LABORABLE && !EsLaborable(dia, delegacion, esFestivo); i++)
            {
                dia = dia.AddDays(1);
            }
            return dia;
        }

        private const int DIAS_MAXIMOS_BUSQUEDA_LABORABLE = 60;

        /// <summary>NestoAPI#627: ni sábado, ni domingo, ni festivo de la delegación.</summary>
        public static bool EsLaborable(DateTime dia, string delegacion, Func<DateTime, string, bool> esFestivo)
        {
            return dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && !esFestivo(dia, delegacion);
        }

        /// <summary>
        /// NestoAPI#779: los festivos de ese año que hay en ESTE fichero (el respaldo de la tabla Festivos mientras no esté en
        /// producción o si le falta un año). Se quitará cuando la tabla esté en producción. Nunca null.
        /// </summary>
        internal static IReadOnlyList<Festivo> FestivosDelCodigo(int anio)
            => DelCodigoPorAnio.Value.TryGetValue(anio, out IReadOnlyList<Festivo> delAnio) ? delAnio : SinFestivos;

        private static readonly IReadOnlyList<Festivo> SinFestivos = new List<Festivo>();
        private static readonly Lazy<Dictionary<int, IReadOnlyList<Festivo>>> DelCodigoPorAnio =
            new Lazy<Dictionary<int, IReadOnlyList<Festivo>>>(() => FiestasDelCodigo
                .GroupBy(f => f.Fecha.Year)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Festivo>)g.ToList()));

        /// <summary>NestoAPI#779: toda la lista del código (para comprobar que el script carga exactamente lo mismo).</summary>
        internal static IReadOnlyList<Festivo> TodosLosFestivosDelCodigo => FiestasDelCodigo;

        // NestoAPI#779: el respaldo de la tabla Festivos (Scripts/Issue779_Festivos.sql la carga con EXACTAMENTE esto).
        private static readonly List<Festivo> FiestasDelCodigo = new List<Festivo>
        {
            #region 2024
            new Festivo()
            {
                Fecha = new DateTime(2024, 1, 1),
                Fiesta = "Año nuevo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 1, 6),
                Fiesta = "Epifanía del Señor",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 3, 28),
                Fiesta = "Jueves Santo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 3, 29),
                Fiesta = "Viernes Santo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 5, 1),
                Fiesta = "Día del trabajo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 5, 2),
                Fiesta = "Día de la Comunidad de Madrid",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 7, 25),
                Fiesta = "Santiago Apostol",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 8, 15),
                Fiesta = "Asunción de la Virgen",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 10, 12),
                Fiesta = "Fiesta Nacional Española",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 1, 11),
                Fiesta = "Día de Todos los Santos",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 12, 6),
                Fiesta = "Día de la Constitución Española",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 12, 25),
                Fiesta = "Navidad",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 5, 15),
                Fiesta = "San Isidro Labrador",
                TipoFestivo = TipoFestivo.Madrid
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 11, 9),
                Fiesta = "Nuestra Señora de la Almudena",
                TipoFestivo = TipoFestivo.Madrid
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 1, 24),
                Fiesta = "Nuestra Señora de la Paz",
                TipoFestivo = TipoFestivo.Alcobendas
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 5, 15),
                Fiesta = "San Isidro",
                TipoFestivo = TipoFestivo.Alcobendas
            },
            /*
            new Festivo()
            {
                Fecha = new DateTime(2024, 9, 13),
                Fiesta = "Fiestas de Algete (viernes)",
                TipoFestivo = TipoFestivo.Algete
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 9, 16),
                Fiesta = "Fiestas de Algete (lunes)",
                TipoFestivo = TipoFestivo.Algete
            },
            */
            new Festivo()
            {
                Fecha = new DateTime(2024, 12, 24),
                Fiesta = "Nochebuena",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2024, 12, 31),
                Fiesta = "Nochevieja",
                TipoFestivo = TipoFestivo.Nacional
            },
            #endregion 2024

            #region 2025
            // 2025
            new Festivo()
            {
                Fecha = new DateTime(2025, 1, 1),
                Fiesta = "Año nuevo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 1, 6),
                Fiesta = "Epifanía del Señor",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 4, 17),
                Fiesta = "Jueves Santo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 4, 18),
                Fiesta = "Viernes Santo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 5, 1),
                Fiesta = "Día del trabajo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 8, 15),
                Fiesta = "Asunción de la Virgen",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 11, 1),
                Fiesta = "Día de Todos los Santos",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 12, 6),
                Fiesta = "Día de la Constitución Española",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 12, 8),
                Fiesta = "Inmaculada Concepción",
                TipoFestivo = TipoFestivo.Nacional
            },new Festivo()
            {
                Fecha = new DateTime(2025, 12, 25),
                Fiesta = "Navidad",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 5, 2),
                Fiesta = "Día de la Comunidad de Madrid",
                TipoFestivo = TipoFestivo.Autonómica
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 7, 25),
                Fiesta = "Santiago Apostol",
                TipoFestivo = TipoFestivo.Autonómica
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 5, 15),
                Fiesta = "San Isidro Labrador",
                TipoFestivo = TipoFestivo.Madrid
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 11, 10),
                Fiesta = "Nuestra Señora de la Almudena",
                TipoFestivo = TipoFestivo.Madrid
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 9, 12),
                Fiesta = "Fiestas de Algete (viernes)",
                TipoFestivo = TipoFestivo.Algete
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 9, 15),
                Fiesta = "Fiestas de Algete (lunes)",
                TipoFestivo = TipoFestivo.Algete
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 1, 24),
                Fiesta = "Nuestra Señora de la Paz",
                TipoFestivo = TipoFestivo.Alcobendas
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 5, 15),
                Fiesta = "San Isidro",
                TipoFestivo = TipoFestivo.Alcobendas
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 12, 24),
                Fiesta = "Nochebuena",
                TipoFestivo = TipoFestivo.Empresa
            },
            new Festivo()
            {
                Fecha = new DateTime(2025, 12, 31),
                Fiesta = "Nochevieja",
                TipoFestivo = TipoFestivo.Empresa
            },      
            #endregion 2025

            #region 2026
            new Festivo()
            {
                Fecha = new DateTime(2026, 1, 1),
                Fiesta = "Año nuevo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 1, 6),
                Fiesta = "Epifanía del Señor",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 4, 3),
                Fiesta = "Viernes Santo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 5, 1),
                Fiesta = "Día del trabajo",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 8, 15),
                Fiesta = "Asunción de la Virgen",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 10, 12),
                Fiesta = "Fiesta Nacional de España",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 11, 1),
                Fiesta = "Día de Todos los Santos",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 12, 7),
                Fiesta = "Día de la Constitución Española",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 12, 8),
                Fiesta = "Inmaculada Concepción",
                TipoFestivo = TipoFestivo.Nacional
            },new Festivo()
            {
                Fecha = new DateTime(2026, 12, 25),
                Fiesta = "Navidad",
                TipoFestivo = TipoFestivo.Nacional
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 4, 2),
                Fiesta = "Jueves Santo",
                TipoFestivo = TipoFestivo.Autonómica
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 5, 2),
                Fiesta = "Día de la Comunidad de Madrid",
                TipoFestivo = TipoFestivo.Autonómica
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 11, 2),
                Fiesta = "Traslado de Todos los Santos (Comunidad de Madrid)",
                TipoFestivo = TipoFestivo.Autonómica
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 5, 15),
                Fiesta = "San Isidro Labrador",
                TipoFestivo = TipoFestivo.Madrid
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 11, 10),
                Fiesta = "Nuestra Señora de la Almudena",
                TipoFestivo = TipoFestivo.Madrid
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 9, 11),
                Fiesta = "Fiestas de Algete (viernes)",
                TipoFestivo = TipoFestivo.Algete
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 9, 14),
                Fiesta = "Fiestas de Algete (lunes)",
                TipoFestivo = TipoFestivo.Algete
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 1, 24),
                Fiesta = "Nuestra Señora de la Paz",
                TipoFestivo = TipoFestivo.Alcobendas
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 5, 15),
                Fiesta = "San Isidro",
                TipoFestivo = TipoFestivo.Alcobendas
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 12, 24),
                Fiesta = "Nochebuena",
                TipoFestivo = TipoFestivo.Empresa
            },
            new Festivo()
            {
                Fecha = new DateTime(2026, 12, 31),
                Fiesta = "Nochevieja",
                TipoFestivo = TipoFestivo.Empresa
            }
            #endregion 2026
        };

    }
}