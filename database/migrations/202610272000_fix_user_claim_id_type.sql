-- IdentityUserClaim<TKey>.Id is Int32; TKey only controls user_id.

ALTER TABLE public.user_claims
    ALTER COLUMN id TYPE integer
    USING id::integer;