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

            // Buscar en la lista Fiestas si hay algún elemento que coincida con la fecha y el tipo de festivo
            var festivoEncontrado = Fiestas.Any(f => f.Fecha == fechaSinHora && f.TipoFestivo == TipoFestivo.Nacional);
            if (festivoEncontrado)
            {
                return true;
            }

            // Verificar fiestas según el valor del parámetro "delegacion"
            switch (delegacion)
            {
                case Constantes.Almacenes.ALGETE:
                    return Fiestas.Any(f => f.Fecha == fechaSinHora && (f.TipoFestivo == TipoFestivo.Empresa));
                case Constantes.Almacenes.ALCOBENDAS:
                    return Fiestas.Any(f => f.Fecha == fechaSinHora && (f.TipoFestivo == TipoFestivo.Alcobendas || f.TipoFestivo == TipoFestivo.Empresa));
                case Constantes.Almacenes.REINA:
                    return Fiestas.Any(f => f.Fecha == fechaSinHora && (f.TipoFestivo == TipoFestivo.Madrid || f.TipoFestivo == TipoFestivo.Empresa));
                default:
                    return false;
            }
        }

        private static readonly List<Festivo> Fiestas = new List<Festivo>
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