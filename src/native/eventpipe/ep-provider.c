#include "ep-rt-config.h"

#ifdef ENABLE_PERFTRACING
#if !defined(EP_INCLUDE_SOURCE_FILES) || defined(EP_FORCE_INCLUDE_SOURCE_FILES)

#define EP_IMPL_PROVIDER_GETTER_SETTER
#include "ep.h"
#include "ep-config.h"
#include "ep-config-internals.h"
#include "ep-event.h"
#include "ep-provider.h"
#include "ep-provider-internals.h"
#include "ep-session.h"
#include "ep-rt.h"

/*
 * Forward declares of all static functions.
 */

static
void
DN_CALLBACK_CALLTYPE
event_free_func (void *ep_event);

static
const EventPipeProviderCallbackData *
provider_prepare_callback_data (
	EventPipeProvider *provider,
	int64_t keywords,
	EventPipeEventLevel provider_level,
	const ep_char8_t *filter_data,
	EventPipeProviderCallbackData *provider_callback_data,
	EventPipeSessionID session_id);

// _Requires_lock_held (ep)
static
void
provider_refresh_all_events (
	EventPipeProvider *provider,
	uint64_t session_mask);

// _Requires_lock_held (ep)
static
void
provider_refresh_event_state (
	EventPipeEvent *ep_event,
	uint64_t session_mask);

// Compute the enabled bit mask, the ith bit is 1 iff an event with the
// given (provider, keywords, eventLevel) is enabled for the ith session.
// _Requires_lock_held (ep)
static
int64_t
provider_compute_event_enable_mask (
	const EventPipeConfiguration *config,
	const EventPipeProvider *provider,
	const EventPipeEvent *ep_event);

/*
 * EventPipeProvider.
 */

static
void
DN_CALLBACK_CALLTYPE
event_free_func (void *ep_event)
{
	ep_event_free ((EventPipeEvent *)ep_event);
}

static
const EventPipeProviderCallbackData *
provider_prepare_callback_data (
	EventPipeProvider *provider,
	int64_t keywords,
	EventPipeEventLevel provider_level,
	const ep_char8_t *filter_data,
	EventPipeProviderCallbackData *provider_callback_data,
	EventPipeSessionID session_id)
{
	EP_ASSERT (provider != NULL);
	EP_ASSERT (provider_callback_data != NULL);

	ep_requires_lock_held ();

	if (provider->callback_func != NULL)
		provider->callbacks_pending++;

	return ep_provider_callback_data_init (
		provider_callback_data,
		filter_data,
		provider->callback_func,
		provider->callback_data,
		keywords,
		provider_level,
		provider->sessions != 0,
		session_id,
		provider);
}

static
void
provider_refresh_all_events (
	EventPipeProvider *provider,
	uint64_t session_mask)
{
	EP_ASSERT (provider != NULL);

	ep_requires_lock_held ();

	EP_ASSERT (provider->event_list != NULL);

	DN_LIST_FOREACH_BEGIN (EventPipeEvent *, current_event, provider->event_list) {
		provider_refresh_event_state (current_event, session_mask);
	} DN_LIST_FOREACH_END;

	ep_requires_lock_held ();
	return;
}

static
void
provider_refresh_event_state (
	EventPipeEvent *ep_event,
	uint64_t session_mask)
{
	EP_ASSERT (ep_event != NULL);

	ep_requires_lock_held ();

	EventPipeProvider *provider = ep_event_get_provider (ep_event);
	EP_ASSERT (provider != NULL);

	EventPipeConfiguration *config = provider->config;
	EP_ASSERT (config != NULL);

	int64_t enable_mask = provider_compute_event_enable_mask (config, provider, ep_event);
	ep_event_set_enabled_mask (ep_event, enable_mask);

	// If session_mask is not zero, that session is being enabled/disabled.
	// We need to unset the metadata_written_mask for this session.
	if (session_mask != 0)
		ep_event_update_metadata_written_mask (ep_event, session_mask, false);

	ep_requires_lock_held ();
	return;
}

static
int64_t
provider_compute_event_enable_mask (
	const EventPipeConfiguration *config,
	const EventPipeProvider *provider,
	const EventPipeEvent *ep_event)
{
	EP_ASSERT (provider != NULL);

	ep_requires_lock_held ();

	int64_t result = 0;
	bool provider_enabled = ep_provider_get_enabled (provider);
	if (!provider_enabled)
		return result;

	for (int i = 0; i < EP_MAX_NUMBER_OF_SESSIONS; i++) {
		// Entering EventPipe lock gave us a barrier, we don't need more of them.
		EventPipeSession *session = ep_volatile_load_session_without_barrier (i);
		if (session == NULL)
			continue;

		EventPipeSessionProvider *session_provider = config_get_session_provider (config, session, provider);
		if (session_provider == NULL)
			continue;

		if (ep_session_provider_allows_event (session_provider, ep_event))
			result = result | ep_session_get_mask (session);
	}

	ep_requires_lock_held ();
	return result;
}

EventPipeProvider *
ep_provider_alloc (
	EventPipeConfiguration *config,
	const ep_char8_t *provider_name,
	EventPipeCallback callback_func,
	void *callback_data)
{
	EP_ASSERT (config != NULL);
	EP_ASSERT (provider_name != NULL);

	EventPipeProvider *instance = ep_rt_object_alloc (EventPipeProvider);
	ep_raise_error_if_nok (instance != NULL);

	instance->provider_name = ep_rt_utf8_string_dup (provider_name);
	ep_raise_error_if_nok (instance->provider_name != NULL);

	instance->provider_name_utf16 = ep_rt_utf8_to_utf16le_string (provider_name);
	ep_raise_error_if_nok (instance->provider_name_utf16 != NULL);

	instance->event_list = dn_list_alloc ();
	ep_raise_error_if_nok (instance->event_list != NULL);

	ep_rt_wait_event_alloc (&instance->callbacks_complete_event, true /* bManual */, false /* bInitial */);
	ep_raise_error_if_nok (ep_rt_wait_event_is_valid (&instance->callbacks_complete_event));

	instance->keywords = 0;
	instance->provider_level = EP_EVENT_LEVEL_CRITICAL;
	instance->callback_func = callback_func;
	instance->callback_data = callback_data;
	instance->config = config;
	instance->delete_deferred = false;
	instance->sessions = 0;
	instance->callbacks_pending = 0;

ep_on_exit:
	return instance;

ep_on_error:
	ep_provider_free (instance);
	instance = NULL;
	ep_exit_error_handler ();
}

void
ep_provider_free (EventPipeProvider * provider)
{
	ep_return_void_if_nok (provider != NULL);

	ep_requires_lock_not_held ();

	if (provider->event_list) {
		EP_LOCK_ENTER (section1)
			dn_list_custom_free (provider->event_list, event_free_func);
			provider->event_list = NULL;
		EP_LOCK_EXIT (section1)
	}

ep_on_exit:
	ep_rt_wait_event_free (&provider->callbacks_complete_event);
	ep_rt_utf16_string_free (provider->provider_name_utf16);
	ep_rt_utf8_string_free (provider->provider_name);
	ep_rt_object_free (provider);

	ep_requires_lock_not_held ();
	return;

ep_on_error:
	ep_exit_error_handler ();
}

EventPipeEvent *
ep_provider_add_event (
	EventPipeProvider *provider,
	uint32_t event_id,
	uint64_t keywords,
	uint32_t event_version,
	EventPipeEventLevel level,
	bool need_stack,
	const uint8_t *metadata,
	uint32_t metadata_len)
{
	EP_ASSERT (provider != NULL);

	ep_requires_lock_not_held ();

	// Keyword bits 44-47 are reserved for use by EventSources, and every EventSource sets them all.
	// We filter out those bits here so later comparisons don't have to take them in to account. Without
	// filtering, EventSources wouldn't show up with Keywords=0.
	uint64_t session_mask = ~0xF00000000000;
	// -1 is special, it means all keywords. Don't change it.
	uint64_t all_keywords = (uint64_t)(-1);
	if (keywords != all_keywords) {
		keywords &= session_mask;
	}

	EventPipeEvent *instance = ep_event_alloc (
		provider,
		keywords,
		event_id,
		event_version,
		level,
		need_stack,
		metadata,
		metadata_len);

	ep_return_null_if_nok (instance != NULL);

	// Take the config lock before inserting a new event.
	EP_LOCK_ENTER (section1)
		ep_raise_error_if_nok_holding_lock (dn_list_push_back (provider->event_list, instance), section1);
		provider_refresh_event_state (instance, 0);
	EP_LOCK_EXIT (section1)

ep_on_exit:
	ep_requires_lock_not_held ();
	return instance;

ep_on_error:
	ep_event_free (instance);
	instance = NULL;
	ep_exit_error_handler ();
}

void
ep_provider_set_delete_deferred (
	EventPipeProvider *provider,
	bool deferred)
{
	EP_ASSERT (provider != NULL);
	provider->delete_deferred = deferred;

	provider->callback_func = NULL;
	provider->callback_data = NULL;
}

const EventPipeProviderCallbackData *
provider_set_config (
	EventPipeProvider *provider,
	int64_t keywords_for_all_sessions,
	EventPipeEventLevel level_for_all_sessions,
	uint64_t session_mask,
	int64_t keywords,
	EventPipeEventLevel level,
	const ep_char8_t *filter_data,
	EventPipeProviderCallbackData *callback_data,
	EventPipeSessionID session_id)
{
	EP_ASSERT (provider != NULL);
	EP_ASSERT ((provider->sessions & session_mask) == 0);

	ep_requires_lock_held ();

	provider->sessions = (provider->sessions | session_mask);
	provider->keywords = keywords_for_all_sessions;
	provider->provider_level = level_for_all_sessions;

	provider_refresh_all_events (provider, session_mask);
	provider_prepare_callback_data (provider, provider->keywords, provider->provider_level, filter_data, callback_data, session_id);

	ep_requires_lock_held ();
	return callback_data;
}

const EventPipeProviderCallbackData *
provider_unset_config (
	EventPipeProvider *provider,
	int64_t keywords_for_all_sessions,
	EventPipeEventLevel level_for_all_sessions,
	uint64_t session_mask,
	int64_t keywords,
	EventPipeEventLevel level,
	const ep_char8_t *filter_data,
	EventPipeProviderCallbackData *callback_data)
{
	ep_requires_lock_held ();

	ep_return_null_if_nok (provider != NULL);

	EP_ASSERT ((provider->sessions & session_mask) != 0);
	if (provider->sessions & session_mask)
		provider->sessions = (provider->sessions & ~session_mask);

	provider->keywords = keywords_for_all_sessions;
	provider->provider_level = level_for_all_sessions;

	provider_refresh_all_events (provider, session_mask);
	provider_prepare_callback_data (provider, provider->keywords, provider->provider_level, filter_data, callback_data, (EventPipeSessionID)0);

	ep_requires_lock_held ();
	return callback_data;
}

void
provider_invoke_callback (EventPipeProviderCallbackData *provider_callback_data)
{
	EP_ASSERT (provider_callback_data != NULL);

	// A lock should not be held when invoking the callback, as concurrent callbacks
	// may trigger a deadlock with the EventListenersLock as detailed in
	// https://github.com/dotnet/runtime/pull/105734
	ep_requires_lock_not_held ();

	const ep_char8_t *filter_data = ep_provider_callback_data_get_filter_data (provider_callback_data);
	EventPipeCallback callback_function = ep_provider_callback_data_get_callback_function (provider_callback_data);
	bool enabled = ep_provider_callback_data_get_enabled (provider_callback_data);
	int64_t keywords = ep_provider_callback_data_get_keywords (provider_callback_data);
	EventPipeEventLevel provider_level = ep_provider_callback_data_get_provider_level (provider_callback_data);
	void *callback_data = ep_provider_callback_data_get_callback_data (provider_callback_data);
	EventPipeSessionID session_id = ep_provider_callback_data_get_session_id (provider_callback_data);

	bool is_event_filter_desc_init = false;
	EventFilterDescriptor event_filter_desc;
	uint8_t *buffer = NULL;

	if (filter_data) {
		// The callback is expecting that filter data to be a concatenated list
		// of pairs of null terminated strings. The first member of the pair is
		// the key and the second is the value.
		// To convert to this format we need to convert all '=' and ';'
		// characters to '\0', except when in a quoted string.
		const uint32_t filter_data_len = (uint32_t)strlen (filter_data);
		uint32_t buffer_size = filter_data_len + 1;

		buffer = ep_rt_byte_array_alloc (buffer_size);
		ep_raise_error_if_nok (buffer != NULL);

		bool is_quoted_value = false;
		uint32_t j = 0;

		for (uint32_t i = 0; i < buffer_size; ++i) {
			// if a value is a quoted string, leave the quotes out from the destination
			// and don't replace `=` or `;` characters until leaving the quoted section
			// e.g., key="a;value=";foo=bar --> { key\0a;value=\0foo\0bar\0 }
			if (filter_data [i] == '"') {
				is_quoted_value = !is_quoted_value;
				continue;
			}
			buffer [j++] = ((filter_data [i] == '=' || filter_data [i] == ';') && !is_quoted_value) ? '\0' : filter_data [i];
		}

		// In case we skipped over quotes in the filter string, shrink the buffer size accordingly
		if (j < filter_data_len)
			buffer_size = j + 1;

		ep_event_filter_desc_init (&event_filter_desc, (uint64_t)buffer, buffer_size, /* EventFilterType.StringKeyValueEncoding */ 0);
		is_event_filter_desc_init = true;
	}

	// NOTE: When we call the callback, we pass in enabled (which is either 1 or 0) as the ControlCode.
	// If we want to add new ControlCode, we have to make corresponding change in ETW callback signature
	// to address this. See https://github.com/dotnet/runtime/pull/36733 for more discussions on this.
	if (callback_function && !ep_rt_process_shutdown ()) {
		ep_rt_provider_invoke_callback (
			callback_function,
			(uint8_t *)(session_id == 0 ? NULL : &session_id), /* session_id */
			enabled ? 1 : 0, /* ControlCode */
			(uint8_t)provider_level,
			(uint64_t)keywords,
			0, /* match_all_keywords */
			is_event_filter_desc_init ? &event_filter_desc : NULL,
			callback_data /* CallbackContext */);
	}

	// The callback completed, can take the lock again.
	EP_LOCK_ENTER (section1)
		if (callback_function != NULL) {
			EventPipeProvider *provider = provider_callback_data->provider;
			provider->callbacks_pending--;
			if (provider->callbacks_pending == 0 && provider->callback_func == NULL) {
				// ep_delete_provider deferred provider deletion and is waiting for all in-flight callbacks
				// to complete. This is the last callback, so signal completion.
				ep_rt_wait_event_set (&provider->callbacks_complete_event);
			}
		}
	EP_LOCK_EXIT (section1)

ep_on_exit:
	if (is_event_filter_desc_init)
		ep_event_filter_desc_fini (&event_filter_desc);

	ep_rt_byte_array_free (buffer);
	return;

ep_on_error:
	ep_exit_error_handler ();
}

EventPipeProvider *
provider_create_register (
	const ep_char8_t *provider_name,
	EventPipeCallback callback_func,
	void *callback_data,
	EventPipeProviderCallbackDataQueue *provider_callback_data_queue)
{
	ep_requires_lock_held ();
	return config_create_provider (ep_config_get (), provider_name, callback_func, callback_data, provider_callback_data_queue);
}

void
provider_unregister_delete (EventPipeProvider * provider)
{
	ep_return_void_if_nok (provider != NULL);

	ep_requires_lock_held ();
	config_delete_provider (ep_config_get (), provider);
}

void
provider_free (EventPipeProvider * provider)
{
	ep_return_void_if_nok (provider != NULL);

	ep_requires_lock_held ();

	dn_list_custom_free (provider->event_list, event_free_func);

	ep_rt_wait_event_free (&provider->callbacks_complete_event);
	ep_rt_utf16_string_free (provider->provider_name_utf16);
	ep_rt_utf8_string_free (provider->provider_name);
	ep_rt_object_free (provider);
}

EventPipeEvent *
provider_add_event (
	EventPipeProvider *provider,
	uint32_t event_id,
	uint64_t keywords,
	uint32_t event_version,
	EventPipeEventLevel level,
	bool need_stack,
	const uint8_t *metadata,
	uint32_t metadata_len)
{
	EP_ASSERT (provider != NULL);

	ep_requires_lock_held ();

	EventPipeEvent *instance = ep_event_alloc (
		provider,
		keywords,
		event_id,
		event_version,
		level,
		need_stack,
		metadata,
		metadata_len);

	ep_raise_error_if_nok (instance != NULL);

	ep_raise_error_if_nok (dn_list_push_back (provider->event_list, instance));
	provider_refresh_event_state (instance, 0);

ep_on_exit:
	ep_requires_lock_held ();
	return instance;

ep_on_error:
	ep_event_free (instance);
	instance = NULL;
	ep_exit_error_handler ();
}

#endif /* !defined(EP_INCLUDE_SOURCE_FILES) || defined(EP_FORCE_INCLUDE_SOURCE_FILES) */
#endif /* ENABLE_PERFTRACING */

#if !defined(ENABLE_PERFTRACING) || (defined(EP_INCLUDE_SOURCE_FILES) && !defined(EP_FORCE_INCLUDE_SOURCE_FILES))
extern const char quiet_linker_empty_file_warning_eventpipe_provider;
const char quiet_linker_empty_file_warning_eventpipe_provider = 0;
#endif
