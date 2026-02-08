import os
import sqlite3
from datetime import datetime, date, time

import pandas as pd
import streamlit as st
import plotly.graph_objects as go

try:
    from streamlit_plotly_events import plotly_events
    HAS_PLOTLY_EVENTS = True
except Exception:
    HAS_PLOTLY_EVENTS = False

st.markdown(
    """
<style>
    .stApp { background-color: #0f1117; color: #e6e6e6; }
    [data-testid="stSidebar"] { background-color: #121722; }
    [data-testid="stSidebar"] * { color: #e6e6e6; }
    h1, h2, h3, h4, h5, h6 { color: #f2f2f2; }
    .stButton>button { background: #2a3348; color: #e6e6e6; border: 1px solid #3a4766; }
    .stButton>button:hover { background: #34405c; border-color: #4b5a7d; }
    .stSelectbox, .stDateInput, .stTimeInput { color: #e6e6e6; }
    input, textarea, select { color: #111111 !important; background-color: #ffffff !important; }
    [data-baseweb="input"] input { color: #111111 !important; background-color: #ffffff !important; }
    [data-baseweb="select"] input { color: #111111 !important; background-color: #ffffff !important; }
    [data-baseweb="select"] div { color: #111111 !important; }
</style>
""",
    unsafe_allow_html=True,
)
DB_DIR = os.environ.get(
    "ZEN_HISDB_DIR",
    r"D:\Project\ZenPlatform\bin\Debug\net8.0-windows\回測歷史資料庫",
)
PRODUCTS = {
    "TX (大台)": 1,
    "MTX (小台)": 2,
    "TMF": 3,
}

st.set_page_config(page_title="Historical K-Chart", layout="wide")
st.title("Historical K-Chart Viewer")


def list_db_files(db_dir: str):
    if not os.path.isdir(db_dir):
        return []
    files = [f for f in os.listdir(db_dir) if f.endswith(".db")]
    files.sort()
    return files


def extract_year(db_file: str):
    # Expect format: 歷史價格資料庫.YYYY.db
    try:
        parts = db_file.split(".")
        for part in parts:
            if part.isdigit() and len(part) == 4:
                return int(part)
    except Exception:
        pass
    return None


@st.cache_data(show_spinner=False)
def get_available_range(db_path: str, product: int):
    query = "SELECT MIN(ts) AS min_ts, MAX(ts) AS max_ts FROM bars_1m WHERE product = ?"
    with sqlite3.connect(db_path) as conn:
        row = conn.execute(query, (product,)).fetchone()
    if not row or row[0] is None or row[1] is None:
        return None, None
    return int(row[0]), int(row[1])


@st.cache_data(show_spinner=False)
def load_bars_1m(db_path: str, product: int, start_ts: int, end_ts: int):
    query = (
        "SELECT ts, open, high, low, close, volume "
        "FROM bars_1m "
        "WHERE product = ? AND ts BETWEEN ? AND ? "
        "ORDER BY ts"
    )
    with sqlite3.connect(db_path) as conn:
        df = pd.read_sql_query(query, conn, params=(product, start_ts, end_ts))
    if df.empty:
        return df
    for col in ("open", "high", "low", "close", "volume"):
        df[col] = pd.to_numeric(df[col], errors="coerce")
    df["dt"] = pd.to_datetime(df["ts"], unit="s")
    df = df.set_index("dt")
    return df


def resample_bars(df: pd.DataFrame, minutes: int):
    if df.empty:
        return df
    rule = f"{minutes}min"
    agg = {
        "open": "first",
        "high": "max",
        "low": "min",
        "close": "last",
        "volume": "sum",
    }
    out = df.resample(rule, label="right", closed="right").agg(agg)
    out = out.dropna()
    return out


def build_chart(df: pd.DataFrame, title: str, y_range=None):
    fig = go.Figure()
    fig.add_trace(
        go.Candlestick(
            x=df.index.to_pydatetime(),
            open=df["open"],
            high=df["high"],
            low=df["low"],
            close=df["close"],
            name="Price",
        )
    )
    fig.update_layout(
        title=title,
        xaxis_title="Time",
        yaxis_title="Price",
        height=600,
        xaxis_rangeslider_visible=False,
    )
    if y_range:
        fig.update_yaxes(range=y_range)
    return fig


def build_volume(df: pd.DataFrame):
    fig = go.Figure()
    fig.add_trace(
        go.Bar(
            x=df.index.to_pydatetime(),
            y=df["volume"],
            name="Volume",
        )
    )
    fig.update_layout(height=220, xaxis_title="Time", yaxis_title="Volume")
    return fig


with st.sidebar:
    st.header("Data Source")
    db_files = list_db_files(DB_DIR)
    if not db_files:
        st.error(f"DB directory not found or empty: {DB_DIR}")
        st.stop()

    default_db = db_files[-1]
    db_file = st.selectbox("Year DB", db_files, index=len(db_files) - 1, key="db_file")
    product_label = st.selectbox("Product", list(PRODUCTS.keys()), index=0)
    product = PRODUCTS[product_label]

    st.header("Time Range")
    selected_year = extract_year(db_file)
    db_path_for_range = os.path.join(DB_DIR, db_file)
    min_ts, max_ts = get_available_range(db_path_for_range, product)
    if min_ts and max_ts:
        min_dt = datetime.fromtimestamp(min_ts)
        max_dt = datetime.fromtimestamp(max_ts)
    else:
        min_dt = max_dt = None

    if "start_date" not in st.session_state or "end_date" not in st.session_state:
        if min_dt and max_dt:
            st.session_state.setdefault("start_date", min_dt.date())
            st.session_state.setdefault("end_date", max_dt.date())
        elif selected_year:
            st.session_state.setdefault("start_date", date(selected_year, 1, 1))
            st.session_state.setdefault("end_date", date(selected_year, 12, 31))
        else:
            today = date.today()
            st.session_state.setdefault("start_date", date(today.year, 1, 1))
            st.session_state.setdefault("end_date", today)

    # Auto-reset dates when year/product changes
    year_key = (selected_year, product_label)
    if st.session_state.get("last_year_key") != year_key:
        if min_dt and max_dt:
            st.session_state["start_date"] = min_dt.date()
            st.session_state["end_date"] = max_dt.date()
        elif selected_year:
            st.session_state["start_date"] = date(selected_year, 1, 1)
            st.session_state["end_date"] = date(selected_year, 12, 31)
        st.session_state.last_year_key = year_key

    start_date = st.date_input("Start Date", key="start_date")
    end_date = st.date_input("End Date", key="end_date")
    start_time = st.time_input("Start Time", time(0, 0))
    end_time = st.time_input("End Time", time(23, 59))

    st.header("K-Bar")
    period = st.selectbox("Period (minutes)", [1, 3, 5, 10, 15, 30, 60], index=2)
    auto_y = st.checkbox("Auto Y-scale on zoom", value=True)
    load_btn = st.button("Load")

if load_btn:
    if end_date < start_date:
        st.error("End Date must be >= Start Date")
        st.stop()

    start_dt = datetime.combine(start_date, start_time)
    end_dt = datetime.combine(end_date, end_time)
    start_ts = int(start_dt.timestamp())
    end_ts = int(end_dt.timestamp())

    db_path = os.path.join(DB_DIR, db_file)
    if min_dt and max_dt:
        st.caption(f"Available range: {min_dt} -> {max_dt}")
    st.info(f"Loading {db_file} | {product_label} | {start_dt} -> {end_dt}")

    df_1m = load_bars_1m(db_path, product, start_ts, end_ts)
    st.caption(f"1m rows: {len(df_1m)}")
    if df_1m.empty:
        st.warning("No data in selected range.")
        st.stop()
    st.caption(f"1m sample: {df_1m.index.min()} -> {df_1m.index.max()}")

    if period == 1:
        df = df_1m
    else:
        df = resample_bars(df_1m, period)
    df = df.dropna(subset=["open", "high", "low", "close"])
    st.caption(f"resampled rows: {len(df)}")
    if df.empty:
        st.warning("No bars after resample. Try a shorter range or period=1.")
        st.stop()

    st.subheader("K-Chart")
    view_range = st.session_state.get("view_range")
    df_view = df
    if view_range:
        x0, x1 = view_range
        df_view = df[(df.index >= x0) & (df.index <= x1)]
    if df_view.empty:
        df_view = df

    y_min = df_view["low"].min()
    y_max = df_view["high"].max()
    y_range = None
    if pd.notna(y_min) and pd.notna(y_max):
        y_padding = (y_max - y_min) * 0.05 if y_max > y_min else 1
        y_range = [y_min - y_padding, y_max + y_padding]

    df = df.reset_index()
    df = df.rename(columns={"dt": "timestamp", "index": "timestamp"})
    df["timestamp"] = pd.to_datetime(df["timestamp"], errors="coerce")
    df = df.dropna(subset=["timestamp"])
    fig = go.Figure()
    fig.add_trace(
        go.Candlestick(
            x=df["timestamp"],
            open=df["open"],
            high=df["high"],
            low=df["low"],
            close=df["close"],
            name="Price",
            increasing=dict(line=dict(color="#ff4d4f"), fillcolor="#ff4d4f"),
            decreasing=dict(line=dict(color="#3ad071"), fillcolor="#3ad071"),
        )
    )
    fig.update_layout(
        title=f"{product_label} {period}m",
        xaxis_title="Time",
        yaxis_title="Price",
        height=600,
        xaxis_rangeslider_visible=False,
        template="plotly_dark",
        plot_bgcolor="#0f1117",
        paper_bgcolor="#0f1117",
        font=dict(color="#c9d1d9"),
    )
    fig.update_xaxes(showgrid=True, gridcolor="#2a2f3a", zeroline=False)
    fig.update_yaxes(showgrid=True, gridcolor="#2a2f3a", zeroline=False)
    if y_range:
        fig.update_yaxes(range=y_range)
    if auto_y and HAS_PLOTLY_EVENTS:
        events = plotly_events(
            fig,
            click_event=False,
            select_event=False,
            hover_event=False,
            override_height=600,
            key="kchart",
        )
        relayout = st.session_state.get("plotly_relayout")
        if isinstance(relayout, dict):
            x0 = relayout.get("xaxis.range[0]")
            x1 = relayout.get("xaxis.range[1]")
            autorange = relayout.get("xaxis.autorange")
            if autorange:
                st.session_state.view_range = None
            elif x0 and x1:
                try:
                    st.session_state.view_range = (pd.to_datetime(x0), pd.to_datetime(x1))
                except Exception:
                    st.session_state.view_range = None
    else:
        st.plotly_chart(fig, width="stretch")
        if auto_y and not HAS_PLOTLY_EVENTS:
            st.info("Auto Y-scale on zoom needs streamlit-plotly-events. Install it to enable.")

    st.subheader("Volume")
    vol_fig = go.Figure()
    vol_fig.add_trace(
        go.Bar(x=df["timestamp"], y=df["volume"], name="Volume", marker_color="#5f6a7d")
    )
    vol_fig.update_layout(
        height=220,
        xaxis_title="Time",
        yaxis_title="Volume",
        template="plotly_dark",
        plot_bgcolor="#0f1117",
        paper_bgcolor="#0f1117",
        font=dict(color="#c9d1d9"),
    )
    vol_fig.update_xaxes(showgrid=True, gridcolor="#2a2f3a", zeroline=False)
    vol_fig.update_yaxes(showgrid=True, gridcolor="#2a2f3a", zeroline=False)
    st.plotly_chart(vol_fig, width="stretch")

    st.caption(f"Rows: {len(df)}")
else:
    st.info("Set parameters and click Load.")
